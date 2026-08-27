# MintPlayer.AspNetCore.LoggerProviders

A `Microsoft.Extensions.Logging` provider that writes structured entries — timestamp, level,
category, event id, scopes, message and **exception** — to a file.

## Installation

```bash
dotnet add package MintPlayer.AspNetCore.LoggerProviders
```

## Quick start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFileProvider(options =>
{
    // Required. Resolve it against a root you control — see "The filename" below.
    options.FileName = Path.Combine(builder.Environment.ContentRootPath, "Log.txt");
});
```

There is also a parameterless `AddFileProvider()` for hosts that configure the options elsewhere
(`services.Configure<FileLoggerOptions>(…)`, or binding a configuration section).

Calling `AddFileProvider()` twice is harmless: the provider is registered at most once, so no entry
is ever written twice.

## Options

| Option | Default | Meaning |
|---|---|---|
| `FileName` | *(none — required)* | Path of the log file. Only `.txt` and `.log` are accepted, compared case-insensitively. |
| `MinimumLevel` | `LogLevel.Trace` | Lowest level this provider writes, independently of any `ILoggingBuilder` filter. `LogLevel.None` disables the provider. |
| `IncludeScopes` | `true` | Whether active `BeginScope` state is written as part of each entry. |

Bound from configuration by property name, e.g.

```json
{ "FileLogger": { "FileName": "Log.txt", "MinimumLevel": "Information", "IncludeScopes": true } }
```

```csharp
builder.Services.AddOptions<FileLoggerOptions>().Bind(builder.Configuration.GetSection("FileLogger"));
builder.Logging.AddFileProvider();
```

## The entry format

One header line per entry, followed by the exception's own lines if there is one, followed by a
blank line (the blank line is what delimits a multi-line entry):

```
<timestamp> [<Level>] <Category>[<EventId>[:<EventName>]] [=> <scope>]* <message>
<exception.ToString()>
<blank>
```

For example:

```
2026-08-27T09:15:42.1234567Z [Error] MintPlayer.Sample.Worker[4711:JobFailed] => RequestId:abc request failed
System.InvalidOperationException: the real cause
   at MintPlayer.Sample.Worker.Run()

```

- **timestamp** — UTC, round-trip (`"O"`) format, invariant culture. It comes from the
  `TimeProvider` registered in the container, or the system clock when there is none, so a test can
  pin it.
- **level** — the `LogLevel` name: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`.
- **category** — omitted when empty.
- **event id** — omitted when the id is `0` and unnamed; written as `[42]` or `[42:TheName]`.
- **scopes** — outermost first, each prefixed with ` => `; omitted when `IncludeScopes` is off.
- **exception** — `exception.ToString()`, so type, message, stack trace and inner exceptions are all
  kept. The framework's default message formatter ignores the exception argument, so a logger that
  writes only the formatted message loses the one thing a log file exists for.

Every field is formatted with the invariant culture, so a file reads identically whatever the host's
culture and time zone.

## The filename

`FileName` has **no default**. An unconfigured provider throws (an `OptionsValidationException`,
when logging is first resolved) instead of quietly writing a relative `Log.txt` into whatever
`Directory.GetCurrentDirectory()` happens to be — which under IIS or a Windows Service is neither
the content root nor necessarily writable. Point it at a root you control:
`ContentRootPath`, a configured log directory, or an absolute path.

A relative path is accepted if you ask for one explicitly, and is resolved to an absolute path
**once**, when the provider is created; the log file therefore never moves if the process later
changes its working directory. The directory must exist — the provider does not create it.

Everything about the filename (presence, extension, resolution) is validated once, at that point,
rather than on every write.

## Concurrency and disposal

Writes are serialised on a lock shared by every logger targeting the same resolved path, and each
entry is a single `open, append, close` with `FileShare.ReadWrite`. So:

- concurrent loggers cannot lose an entry to a sharing violation;
- readers are never locked out — `File.ReadAllText`, an editor or `tail` can read the file while the
  application is running;
- nothing is buffered, so an entry is on disk before `Log` returns, and disposing the provider owes
  no flush.

A foreign process that holds the file with a share mode excluding writers still wins; the resulting
`IOException` surfaces to the caller rather than being swallowed.

## Scopes

The provider implements `ISupportExternalScope`, so it writes the logger factory's shared scope
stack: a scope opened anywhere is recorded on every entry made inside it, whatever the category.
`BeginScope` never returns `null` (with `IncludeScopes = false` it returns an inert scope).

## Registering your own provider

```csharp
builder.Logging.TryAddProvider(serviceProvider => new MyLoggerProvider(...));
```

Registers the provider type at most once. Deduplicating matters here: a logger provider registered
twice makes the factory write every entry twice, and registering twice is easy — a library's own
`AddX()` and the app's explicit call cannot see each other.
