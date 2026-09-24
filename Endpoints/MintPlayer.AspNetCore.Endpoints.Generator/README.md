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
assembly metadata-only, and the generator writes an `internal sealed partial class …Client` from the
endpoint contracts in it. See "Typed client in another project" in the main package's README for the
full rules and the MPEP021–MPEP023 diagnostics.
