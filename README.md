# Orknux for Windows

Lets your Orknux agents use your own Windows computer the way you do: list the
windows, start a program, read a window, click, type, press shortcuts, scroll
and take screenshots.

## Using it

1. Run `OrknuxForWindows.exe`. It needs nothing installed: .NET comes inside it.
2. Enter your server's address and a **read and write** access token from your
   Orknux profile page, and press **Connect**. The token is kept encrypted for
   your Windows account in `%APPDATA%\Orknux\desktop.json`.
3. On the agent's page, switch on the `desktop_*` tools on its Tools list. They
   are off for every agent until somebody does.

Closing the window keeps it connected in the tray; **Quit** there, or
**Disconnect**, ends it. **Ctrl+Alt+Q** stops the agents at once, whatever has
the focus. While an agent is acting, a strip at the top of the screen says so.

## What an agent can and cannot do

- It works on your real screen, with your real mouse and keyboard, as you.
  Unlike the browser extension there is no separate window for it: do not use
  the computer while it works, and keep Ctrl+Alt+Q in mind.
- Only conversations that are yours reach it: your chats, tasks you started, and
  agents those asked. A workflow step or a Slack thread is told nobody is behind
  it.
- The tools tell the model to ask before it deletes, installs, spends money or
  sends something in your name, and to treat what a window says as the window's
  and never as your instruction - but a model can be talked round, so do not
  connect it to an agent you would not trust with your files.
- It cannot drive its own window, or a program running as administrator while
  it is not (Windows keeps those apart).

## How it works

The app holds a long poll open to `GET /api/desktop/next` on your server - the
same door the Chrome extension uses under `/api/browser`. An agent's
`desktop_*` call is queued there for you; the app takes it, does it, and posts
the answer to `POST /api/desktop/answers/{id}`. Nothing listens on your machine.

A window is read through UI Automation, the interface Windows gives screen
readers, with [FlaUI](https://github.com/FlaUI/FlaUI). Each control comes with a
ref like `e12`, good until the next snapshot of that window, so a click names a
control rather than a position; a position works too, for what UI Automation
cannot see.

## Layout and building

- `src/Orknux.Desktop.Core` - the connection, the protocol, key chords and how a
  window is written out as text. No Windows in it.
- `src/Orknux.Desktop` - the tray app and the UI Automation runner.
- `tests/Orknux.Desktop.Core.Tests` - the core's tests, against a stub server.

Everything builds on any machine with the .NET 10 SDK, Windows or not:

```
dotnet test tests/Orknux.Desktop.Core.Tests
dotnet publish src/Orknux.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Without .NET on the machine, the same through Docker:

```
docker run --rm -v "$PWD:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet test tests/Orknux.Desktop.Core.Tests
```

The UI Automation half only runs on Windows; nothing here exercises it yet.
