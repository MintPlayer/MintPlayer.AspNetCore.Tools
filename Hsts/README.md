# MintPlayer.AspNetCore.Hsts
[![License](https://img.shields.io/badge/License-Apache%202.0-green.svg)](https://opensource.org/licenses/Apache-2.0)

ASP.NET Core middleware that correctly adds the HSTS header using the `OnStarting()` hook.

## Version info
| Package                                 | Release                                                                                                                                                                           | Preview                                                                                                                                                                              | Downloads |
|-----------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------|
| MintPlayer.AspNetCore.Hsts              | [![NuGet Version](https://img.shields.io/nuget/v/MintPlayer.AspNetCore.Hsts.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.Hsts)                           | [![NuGet Version](https://img.shields.io/nuget/vpre/MintPlayer.AspNetCore.Hsts.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.Hsts)                           | [![NuGet](https://img.shields.io/nuget/dt/MintPlayer.AspNetCore.Hsts.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.Hsts) |

## Installation
### NuGet package manager
Open the NuGet package manager and install `MintPlayer.AspNetCore.Hsts` in your project
### Package manager console
Install-Package MintPlayer.AspNetCore.Hsts

## Usage

Map the middleware in the pipeline:

	app.UseImprovedHsts();
