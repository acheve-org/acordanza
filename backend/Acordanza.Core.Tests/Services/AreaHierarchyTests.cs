using Acordanza.Core.Services;
using Shouldly;
using Xunit;
using Node = Acordanza.Core.Services.AreaHierarchy.Node;

namespace Acordanza.Core.Tests.Services;

public class AreaHierarchyTests
{
    // Tree:
    //   1 Galicia (root)
    //     2 Peña Rúa
    //       3 Sector 1
    //         4 Sector 1a
    //     5 Sector 1        (same slug as node 3, but under a different parent — node 2's sibling)
    //   10 Asturias (root, no children)
    private static AreaHierarchy BuildTree() => new([
        new Node(1, "Galicia", "galicia", null),
        new Node(2, "Peña Rúa", "pena-rua", 1),
        new Node(3, "Sector 1", "sector-1", 2),
        new Node(4, "Sector 1a", "sector-1a", 3),
        new Node(5, "Sector 1", "sector-1", 1),
        new Node(10, "Asturias", "asturias", null),
    ]);

    [Fact]
    public void DescendantIds_includes_root_and_full_subtree()
    {
        var tree = BuildTree();

        tree.DescendantIds(1).ShouldBe([1, 2, 5, 3, 4], ignoreOrder: true);
    }

    [Fact]
    public void DescendantIds_of_a_leaf_is_just_itself()
    {
        var tree = BuildTree();

        tree.DescendantIds(4).ShouldBe([4]);
    }

    [Fact]
    public void DescendantIds_of_unknown_id_is_just_itself()
    {
        var tree = BuildTree();

        tree.DescendantIds(999).ShouldBe([999]);
    }

    [Fact]
    public void TopLevelId_walks_up_to_the_root()
    {
        var tree = BuildTree();

        tree.TopLevelId(4).ShouldBe(1);
        tree.TopLevelId(1).ShouldBe(1);
    }

    [Fact]
    public void TopLevelId_of_unknown_id_returns_itself()
    {
        var tree = BuildTree();

        tree.TopLevelId(999).ShouldBe(999);
    }

    [Fact]
    public void Ancestors_are_outermost_first_and_exclude_self()
    {
        var tree = BuildTree();

        var ancestors = tree.Ancestors(4);

        ancestors.Select(n => n.Id).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Ancestors_of_a_root_is_empty()
    {
        var tree = BuildTree();

        tree.Ancestors(1).ShouldBeEmpty();
    }

    [Fact]
    public void NamePath_joins_with_guillemet_separator()
    {
        var tree = BuildTree();

        tree.NamePath(4).ShouldBe("Galicia › Peña Rúa › Sector 1 › Sector 1a");
    }

    [Fact]
    public void SlugPath_joins_with_slash()
    {
        var tree = BuildTree();

        tree.SlugPath(4).ShouldBe("galicia/pena-rua/sector-1/sector-1a");
    }

    [Fact]
    public void ResolveSlugPath_resolves_segment_by_segment()
    {
        var tree = BuildTree();

        var resolved = tree.ResolveSlugPath(["galicia", "pena-rua", "sector-1", "sector-1a"]);

        resolved.ShouldNotBeNull();
        resolved.Id.ShouldBe(4);
    }

    [Fact]
    public void ResolveSlugPath_disambiguates_same_slug_under_different_parents()
    {
        var tree = BuildTree();

        // node 5's "sector-1" is a sibling of "pena-rua", not a child of it.
        var underGalicia = tree.ResolveSlugPath(["galicia", "sector-1"]);
        var underPenaRua = tree.ResolveSlugPath(["galicia", "pena-rua", "sector-1"]);

        underGalicia.ShouldNotBeNull();
        underGalicia.Id.ShouldBe(5);
        underPenaRua.ShouldNotBeNull();
        underPenaRua.Id.ShouldBe(3);
    }

    [Fact]
    public void ResolveSlugPath_returns_null_on_no_match()
    {
        var tree = BuildTree();

        tree.ResolveSlugPath(["galicia", "does-not-exist"]).ShouldBeNull();
    }

    [Fact]
    public void ResolveSlugPath_returns_null_on_no_segments()
    {
        var tree = BuildTree();

        tree.ResolveSlugPath([]).ShouldBeNull();
    }
}
