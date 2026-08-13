# Windows development

Requirements: Windows and the .NET 10 SDK.

```powershell
cd windows
dotnet restore WebCamKu.sln
dotnet build WebCamKu.sln --no-restore
dotnet run --project WebCamKu.Client\WebCamKu.Client.csproj
```

M0 uses managed WPF only. Visual Studio C++ tooling and native MSBuild are not required until a later video or virtual-camera milestone.

