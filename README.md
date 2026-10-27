## About The Project

Plaat is self hosted music streaming service for your own music collection.

Features:
- Support for most music formats
- Works on Linux and Windows
- A mobile app to stream your collection on your phone
- Add multiple user profiles
- automatically scan folders for new music
- Automatically reconvert files if they are of unsupported format like ALAC
- Keep track of play counts
- Create smart playlists based on complex rules


## Screenshots
![Artist overview](images/artist-view.png)
![Album overview](images/album-view.png)
![All albums](images/all-albums.png)
![Create smart playlist](images/create-playlist.png)

## Getting Started

```bash
dotnet build                                   # build whole solution (Plaat.slnx)
dotnet test                                    # run all test projects
dotnet run --project Plaat.Api                 # run the API
dotnet run --project Plaat.Frontend            # run the Blazor frontend (expects the API reachable at PlaatBackendOptions:BaseUri)
```

### Installation

Plaat consists of two main applications, both the Plaat.Api and Plaat.Frontend projects need to be build using the dotnet SDK.
Adjust the configuration in Appsettings accordingly to your preferences and setup.
If you are planning to host on a remote server you can use the deploy script to automatically build and copy using ssh.
