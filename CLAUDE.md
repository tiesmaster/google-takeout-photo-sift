# CLAUDE.md

Google Takeout Photo Sift: a fast Windows (WPF, .NET 11) viewer for reviewing a Google Photos Takeout
batch before uploading it to Immich. The design is in `PLAN.md`.

## Conventions

- Naming: projects are `GoogleTakeoutPhotoSift.*` (CamelCase of the repo name); namespaces are
  prefixed with `Tiesmaster.` (`Tiesmaster.GoogleTakeoutPhotoSift.Core`), via
  `RootNamespace` in `Directory.Build.props`.
- Keep `GoogleTakeoutPhotoSift.Core` free of WPF: index, sidecar rules, file operations, ExifTool
  and key bindings live there, with tests.
- Never delete or re-encode photos: deletes move to `batches\deleted\`, rotation only changes the
  EXIF Orientation tag through ExifTool.

## Commits and PRs

- Do not add `Co-Authored-By: Claude` trailers or "Generated with Claude Code" lines to commit
  messages or PR descriptions.

## Build and test

```powershell
dotnet build
dotnet test
dotnet watch --project src/GoogleTakeoutPhotoSift.App -- 2016
```
