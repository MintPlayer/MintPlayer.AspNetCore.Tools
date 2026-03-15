namespace MintPlayer.AspNetCore.Endpoints;

public record EndpointDescriptor(string Name, string Path, IEnumerable<string> Methods, Type HandlerType);
