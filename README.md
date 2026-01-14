# SurvipvP

A 2D Multiplayer Survival PvP game built with C# and Raylib.

## Features
- **Multiplayer**: Real-time multiplayer with lag compensation and client-side prediction.
- **Combat**: Melee combat with different weapons (Wood, Stone, Gold swords).
- **Gathering**: Collect resources (Wood, Stone, Gold) from the environment.
- **Crafting**: Craft weapons and fences using gathered resources.
- **Building**: Place fences to create defenses.
- **Customization**: Choose your nickname and character color.
- **Lag Compensation**: Client-side prediction and interpolation for smooth gameplay.
- **LAN Support**: Play with friends on your local network.

## Visuals
The game features a clean 2D aesthetic with:
- Grid-based rendering for motion clarity.
- Visual feedback for gathering and combat.
- Dynamic UI for inventory and crafting.
- Color-coded players and resources.

## How to Play

### Joining a Game (LAN)
1.  **Start the Server**:
    - Build and run the `GameServer` project.
    - Console will show "Server started on port 6767".
2.  **Start the Client**:
    - Build and run the `GameClient` project.
3.  **Startup Screen**:
    - **Nickname**: Type your desired nickname.
    - **Server IP**: Press `Tab` to switch to the IP field. Type the IP address of the server (e.g., `192.168.1.X`, or `127.0.0.1` for local play).
    - **Color**: Use `Left/Right Arrows` to choose your character color.
    - **Join**: Press `Enter` to connect and join the game.

### Controls
- **Movement**: `W`, `A`, `S`, `D`
- **Gather**: `E` (near resources)
- **Attack**: `Left Mouse Button`
- **Rotate**: `Mouse` (character looks at mouse cursor)
- **Hotbar**: `1`, `2`, `3`, `4` (Select items/weapons)
- **Build**: Select Fence (`5`), then `Left Click` to place. `Right Click` to rotate structure.
- **Crafting**: `5` (Craft Fences), or select locked weapon in hotbar (`2`, `3`, `4`) to craft it.
- **Toggle Recipes**: `I`

## Technical Details
- **Architecture**: Authoritative Server with Client-Side Prediction and Interpolation.
- **Networking**: TCP with `MemoryPack` for high-performance zero-copy serialization.
- **Graphics**: `Raylib-cs` for fast 2D rendering.
- **Physics**: Simple AABB/Circle collision detection.

## Requirements
- .NET 9.0 SDK
- Linux/Windows/Mac (Cross-platform support via Raylib-cs)

## Running from Source
```bash
# Run Server
dotnet run --project GameServer

# Run Client
dotnet run --project GameClient

# Published build
./GameClient/publish.sh && ./GameClient/publish_output/GameClient
```
