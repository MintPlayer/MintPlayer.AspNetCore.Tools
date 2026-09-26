# MintPlayer.AspNetCore.Endpoints.Abstractions

The interfaces and attributes of [MintPlayer.AspNetCore.Endpoints](https://www.nuget.org/packages/MintPlayer.AspNetCore.Endpoints):
the endpoint interface ladder (`IEndpoint`, `IGetEndpoint`, `IPostEndpoint`, … and their typed forms),
`IEndpointGroup`, `[MemberOf<TGroup>]`, `[RouteParam]`, `[QueryParam]`, `[EndpointDescriptorName]`,
`[assembly: EndpointsMethodName]`, `[assembly: EndpointTypeArgument<TConstraint, TArgument>]` (closing
generic endpoints), `HttpVerbs` and `EndpointDescriptor`.

You normally do not reference this package directly: `MintPlayer.AspNetCore.Endpoints` references it and
adds the base classes, the source generator and the runtime. See that package's README for the
documentation.
