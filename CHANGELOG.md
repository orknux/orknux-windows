# Changelog

Each release's section is its notes, on GitHub and on orknux.ai. Written
before the tag: the release workflow refuses a tag with no section here, and
one whose version is not the one in `src/Orknux.Desktop/Orknux.Desktop.csproj`.

## 1.0.10

### Added

- `desktop_open` finds an app by the name the Start menu shows, so "musescore"
  or a Store app opens without its program's path.
- The version is in the window's title bar and the tray's tooltip.

### Changed

- Opening an app that is already running brings its window forward rather than
  starting a second copy, unless the agent asks for a new one.
- A window's screenshot is that window, even where another is in front of it.

## 1.0.6 and earlier

See the [GitHub releases](https://github.com/orknux/orknux-windows/releases).
