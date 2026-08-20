namespace Acordanza.Api.Dtos;

// CanDelete answers per photo, not per viewer: a route topo can be deleted by whoever
// uploaded it as well as by anyone who can edit the route (see ToposController), so within
// one route's list the answer differs from row to row. Area media, whose rule is area-wide,
// sets every row from the same check AreaDetailDto.CanManageMedia reports. Same purpose as
// RouteDto.CanValidate: the client shouldn't have to re-derive a rule the server owns, and
// shouldn't offer a button the API will refuse.
public record TopoDto(int Id, string Url, string FileName, DateTimeOffset CreatedAt, string UploadedBy, bool CanDelete);
