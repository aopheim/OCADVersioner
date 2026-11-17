# OCADVersioner

OCADVersioner is a tool for adding version control to your OCAD files. A split view of the old and new version of the map is shown, together with a list view of all symbol changes.
The tool is meant for map drawers, helping them keep track of different versions of the map in the drawing process. Read more about the features of OCADVersioner on its [about page](https://icy-pond-0f7b2aa03.4.azurestaticapps.net/about).

## Development

The app is a Blazor WebAssembly application in [`map-viewer/`](map-viewer). Build and test it with the .NET 10 SDK and the `wasm-tools` workload:

```bash
cd map-viewer
dotnet workload install wasm-tools
dotnet build MapViewer.slnx -c Release
dotnet test MapViewer.slnx -c Release --no-build
```

[`map-viewer/PLAN.md`](map-viewer/PLAN.md) describes how it is put together. The maps are parsed and rendered with code from [Purple Pen](https://github.com/petergolde/PurplePen); see [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

## Disclaimer

OCADVersioner is very much a fun side project, originally meant for personal use. It is currently not meant for professional use. The first version was built on [ocad2geojson](https://github.com/perliedman/ocad2geojson). Hopefully, I will find time to make it ready for professional use.
