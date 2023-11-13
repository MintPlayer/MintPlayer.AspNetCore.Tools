# MintPlayer.AspNetCore.NoSniff
[![NuGet Version](https://img.shields.io/nuget/v/MintPlayer.AspNetCore.NoSniff.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.NoSniff)
[![NuGet](https://img.shields.io/nuget/dt/MintPlayer.AspNetCore.NoSniff.svg?style=flat)](https://www.nuget.org/packages/MintPlayer.AspNetCore.NoSniff)
[![License](https://img.shields.io/badge/License-Apache%202.0-green.svg)](https://opensource.org/licenses/Apache-2.0)

ASP.NET Core middleware that adds the `X-Content-Type-Options: nosniff` header.

## Installation
### NuGet package manager
Open the NuGet package manager and install `MintPlayer.AspNetCore.NoSniff` in your project
### Package manager console
Install-Package MintPlayer.AspNetCore.NoSniff

## Usage

Map the middleware in the pipeline:

	app.UseNoSniff();
