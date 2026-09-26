# MintPlayer.AspNetCore.Endpoints.Generator

The source generator of [MintPlayer.AspNetCore.Endpoints](https://www.nuget.org/packages/MintPlayer.AspNetCore.Endpoints),
as an analyzer-only package with no `FrameworkReference`.

- **In a server, you do not need it**: `MintPlayer.AspNetCore.Endpoints` already carries the same
  generator and its code fix.
- **In a typed client** — Blazor WebAssembly, a console tool, another service — reference this package
  instead. It builds for `browser-wasm`, because it brings no ASP.NET Core with it:

```xml
<PropertyGroup>
  <GenerateEndpointsClient>true</GenerateEndpointsClient>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="MintPlayer.AspNetCore.Endpoints.Generator" Version="…" PrivateAssets="all" />
  <EndpointsServerReference Include="..\MyShop.Api\MyShop.Api.csproj" />
</ItemGroup>
```

`EndpointsServerReference` (from this package's `build` targets) builds the server and references its
assembly metadata-only, and the generator writes an `internal sealed partial class …Client` per server
into `EndpointClients.g.cs`, from the endpoint contracts in it. See "Typed client in another project" in
the main package's README for the full rules and the MPEP021–MPEP023 diagnostics.

## Requirements

A compiler with **Roslyn 5.9 or newer**: the **.NET SDK 10.0.400+ or 11.x**, or **Visual Studio 2026**.
Older compilers are not supported. The generator ships in `analyzers/dotnet/roslyn5.9/cs`, so an older
compiler (the .NET SDK 10.0.1xx ships Roslyn 5.0) skips it without a word: no client is generated, and
the build fails wherever the generated code is used (in a server, first with `CS1061` for the missing
`Map…Endpoints()`). Update the SDK (or pin a newer one in `global.json`); nothing in your code is wrong.
