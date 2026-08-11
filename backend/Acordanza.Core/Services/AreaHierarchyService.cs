using Acordanza.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Core.Services;

// An in-memory snapshot of the whole area tree (id/name/slug/parent only). The tree
// walks every consumer needs — descendant expansion for filters, top-level ancestor
// for expert checks, name/slug paths for DTOs and blob prefixes — live here instead
// of as per-controller copies. Obtain one via AreaHierarchyService.GetAsync().
public sealed class AreaHierarchy
{
    public sealed record Node(int Id, string Name, string Slug, int? ParentAreaId);

    private readonly Dictionary<int, Node> _byId;
    private readonly Dictionary<int, List<int>> _childIdsByParent;

    public AreaHierarchy(IReadOnlyList<Node> nodes)
    {
        _byId = nodes.ToDictionary(n => n.Id);
        _childIdsByParent = nodes
            .Where(n => n.ParentAreaId is not null)
            .GroupBy(n => n.ParentAreaId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());
    }

    // For lookups a foreign key already guarantees (e.g. Route.AreaId); throws on a
    // missing id. Use Find for ids that came from user input.
    public Node this[int id] => _byId[id];

    public Node? Find(int id) => _byId.GetValueOrDefault(id);

    // Breadth-first walk collecting `rootId` and every descendant. Nothing cascades at
    // the DB level (Area.ParentArea is DeleteBehavior.Restrict), so callers use this to
    // query or delete a whole subtree at once.
    public List<int> DescendantIds(int rootId)
    {
        var result = new List<int> { rootId };
        var queue = new Queue<int>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            if (!_childIdsByParent.TryGetValue(queue.Dequeue(), out var children)) continue;
            foreach (var child in children)
            {
                result.Add(child);
                queue.Enqueue(child);
            }
        }
        return result;
    }

    public int TopLevelId(int id)
    {
        var result = id;
        int? currentId = id;
        while (currentId is int aid && _byId.TryGetValue(aid, out var node))
        {
            result = aid;
            currentId = node.ParentAreaId;
        }
        return result;
    }

    // The node's ancestors, outermost (top-level) first, excluding the node itself.
    public List<Node> Ancestors(int id)
    {
        var result = new List<Node>();
        var currentId = Find(id)?.ParentAreaId;
        while (currentId is int aid && _byId.TryGetValue(aid, out var node))
        {
            result.Insert(0, node);
            currentId = node.ParentAreaId;
        }
        return result;
    }

    // "Root › Child › ..." breadcrumb of Names, shown in route/tick DTOs.
    public string NamePath(int id) => string.Join(" › ", PathParts(id, n => n.Name));

    // Same walk joining Slugs with "/" — the blob-name prefix topo/media images
    // under this area are stored under.
    public string SlugPath(int id) => string.Join("/", PathParts(id, n => n.Slug));

    // Resolves a full slug path (e.g. "pena-rua/sector-1") level by level —
    // Area.Slug is only unique among siblings, so each segment is matched
    // against the previous match's children. Null on no match (or no segments).
    public Node? ResolveSlugPath(IEnumerable<string> segments)
    {
        Node? resolved = null;
        int? currentParentId = null;
        foreach (var segment in segments)
        {
            resolved = _byId.Values.FirstOrDefault(n => n.ParentAreaId == currentParentId && n.Slug == segment);
            if (resolved is null) return null;
            currentParentId = resolved.Id;
        }
        return resolved;
    }

    private List<string> PathParts(int id, Func<Node, string> part)
    {
        var parts = new List<string>();
        int? currentId = id;
        while (currentId is int aid && _byId.TryGetValue(aid, out var node))
        {
            parts.Insert(0, part(node));
            currentId = node.ParentAreaId;
        }
        return parts;
    }
}

// Scoped: the snapshot is loaded once per request and shared by every consumer
// (controllers, AreaExpertAuthorizer), replacing the one-full-table-read-per-helper
// pattern the controllers used to have. The snapshot reflects the DB as of the first
// GetAsync() call in the request — callers that mutate areas must not rely on a
// snapshot taken before their own writes.
public class AreaHierarchyService(AppDbContext db)
{
    private AreaHierarchy? _snapshot;

    public async Task<AreaHierarchy> GetAsync() =>
        _snapshot ??= new AreaHierarchy(await db.Areas.AsNoTracking()
            .Select(a => new AreaHierarchy.Node(a.Id, a.Name, a.Slug, a.ParentAreaId))
            .ToListAsync());
}
