namespace Acordanza.Api.Models;

public class Crag
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? ParentCragId { get; set; }
    public Crag? ParentCrag { get; set; }
    public List<Crag> Children { get; set; } = [];
}
