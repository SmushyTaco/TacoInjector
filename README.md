### Installation

1. Download TacoInjector\_\*.zip from the latest release [here](https://github.com/SmushyTaco/TacoInjector/releases/latest/).
2. Extract the zip.
3. Open PowerShell as an Administrator.
4. Run `cd "[PATH HERE]"` with `[PATH HERE]` being replaced with the path to the extracted folder.
5. Run `powershell -ExecutionPolicy Bypass -File .\InstallUnsigned.ps1`
6. Now TacoInjector is installed. Search for TacoInjector and run it!
7. It's recommened to delete the install files now as they're no longer needed.



### Compilation

1. cd into the `TacoInjector` folder.
2. Run `dotnet publish -f net10.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64`
3. Navigate to `TacoInjector\TacoInjector\bin\Release\net10.0-windows10.0.19041.0\win-x64\AppPackages\TacoInjector_*_Test`
4. Copy `TacoInjector_*_Test` and rename it to `TacoInjector_*`.
5. Copy `InstallUnsigned.ps1` into `TacoInjector_*`
6. Zip the `TacoInjector_*` folder up and make a release.

