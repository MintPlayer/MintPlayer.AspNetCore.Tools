# MintPlayer.AspNetCore.SubDirectoryViews

[![License](https://img.shields.io/badge/License-Apache%202.0-green.svg)](https://opensource.org/licenses/Apache-2.0)

Helper library that lets the Razor Views, Razor Pages and their Area equivalents of your ASP.NET Core application reside in a subfolder

## Version info
| Package                                 | Release                                                                                                                                                                           | Preview                                                                                                                                                                              | Downloads |
|-----------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------|
| MintPlayer.AspNetCore.SubDirectoryViews | [![NuGet Version](https://img.shields.io/nuget/v/MintPlayer.AspNetCore.SubDirectoryViews.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.SubDirectoryViews) | [![NuGet Version](https://img.shields.io/nuget/vpre/MintPlayer.AspNetCore.SubDirectoryViews.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.SubDirectoryViews) | [![NuGet](https://img.shields.io/nuget/dt/MintPlayer.AspNetCore.SubDirectoryViews.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.SubDirectoryViews) |

## Installation
### NuGet package manager
Open the NuGet package manager and install MintPlayer.AspNetCore.SubDirectoryViews in your project
### Package manager console
Install-Package MintPlayer.AspNetCore.SubDirectoryViews

## Usage
Call `ConfigureViewsInSubfolder` on your service collection, passing the folder that contains the `Views` folder:

```csharp
builder.Services.ConfigureViewsInSubfolder("Client");
builder.Services.AddControllersWithViews();
```

The order relative to `AddControllersWithViews()` does not matter, and calling it twice is safe (the last call wins).
