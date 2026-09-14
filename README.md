# Nova Client

Nova Client is an experimental Windows Minecraft client/launcher with a Windows 11-inspired desktop UI.

## v0.1 preview

- Native WPF/.NET 8 Windows application
- Dark Nova design with animated page transitions
- Local isolated profiles
- Minecraft version / loader metadata per profile
- Memory controls
- Live Modrinth mod search
- One-click compatible mod download into the selected profile
- Download progress view
- Microsoft device-code OAuth groundwork (requires your own registered Microsoft public-client application ID through `NOVA_MICROSOFT_CLIENT_ID`)
- GitHub Actions self-contained Windows x64 build

## Downloading a test build

Open **Actions → Build Nova Client Windows**, choose the newest successful run, then download the `Nova-Client-Windows-x64` artifact. It contains the self-contained `NovaClient.exe`.

## Current limitation

The preview deliberately does **not** pretend Minecraft launching is complete: the PLAY button prepares/opens the selected instance but the Mojang/Microsoft entitlement flow, game manifests/libraries/assets, Fabric bootstrap and final Java launch command are the next launcher-core milestone.

## Data

Nova stores profiles under `%APPDATA%/NovaClient` and gives every profile its own instance folder.
