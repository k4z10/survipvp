# survipvp

**A high-performance, authoritative-server multiplayer survival sandbox engineered with C# .NET 9.0 and Raylib.**

## Overview

**survipvp** is a technical demonstration of a real-time multiplayer architecture applied to a 2D survival PvP environment. The project prioritizes robust networking fundamentals—specifically lag compensation, client-side prediction, and entity interpolation—to ensure a fluid combat experience over TCP.

Unlike typical peer-to-peer implementations, this project utilizes a dedicated **Authoritative Server** architecture, ensuring state integrity and preventing client-side exploitation. The rendering pipeline is powered by `Raylib-cs`, delivering high-speed, hardware-accelerated 2D graphics with minimal overhead.

## Core Features

### Engineering & Network Architecture

* **Authoritative Server Model:** The server acts as the single source of truth for all game state, physics simulation, and collision resolution.
* **Latency Mitigation:** Implements **Client-Side Prediction** for immediate local feedback and **Entity Interpolation** to smooth out remote player movement, masking network jitter.
* **Zero-Copy Serialization:** Utilizes `MemoryPack` for extremely fast, allocation-free serialization of network packets, minimizing GC pressure during high-frequency tick updates.
* **Cross-Platform Compatibility:** Fully playable on Linux, Windows, and macOS via the .NET Core runtime and Raylib abstractions.

### Gameplay Mechanics

* **Resource Economy:** A gathering loop involving dynamic extraction of Wood, Stone, and Gold to fuel progression.
* **Tiered Combat System:** Melee engagement mechanics featuring weapon progression (Wood  Stone  Gold) with distinct damage attributes.
* **Fortification:** a grid-based building system allowing players to construct defensive perimeters (fences) to control map flow.
* **Customization:** lightweight profile system for nickname assignment and RGB character customization.

## Technical Stack

| Component | Technology | Description |
| --- | --- | --- |
| **Language** | C# (.NET 9.0) | High-performance, modern managed runtime. |
| **Rendering** | Raylib-cs | Hardware-accelerated 2D rendering library. |
| **Networking** | TCP / Sockets | Reliable transport layer. |
| **Serialization** | MemoryPack | High-performance binary serializer. |
| **Physics** | Custom AABB/Circle | Optimized discrete collision detection. |

## Installation & Deployment

### Prerequisites

* **.NET 9.0 SDK**
* **C Compiler** (GCC/Clang) for native Raylib bindings (if not pre-bundled).

### Building from Source

```bash
# Clone the repository
git clone https://github.com/yourusername/survipvp.git
cd survipvp

# 1. Initialize the Server
# The server governs the game state and accepts incoming TCP connections.
dotnet run --project GameServer

# 2. Initialize the Client
# Launch multiple instances to simulate multiplayer locally.
dotnet run --project GameClient

```

### Production Build (Linux)

For a standalone optimized release:

```bash
chmod +x ./GameClient/publish.sh
./GameClient/publish.sh
./GameClient/publish_output/GameClient

```

## User Guide

### Connection Handshake

1. Launch `GameServer`. Verify console output: `Server started on port 6767`.
2. Launch `GameClient`.
3. **Authentication UI**:
* **Nickname**: Enter identifier.
* **Endpoint**: Press `Tab`. Input target IP (Localhost: `127.0.0.1` or LAN: `192.168.x.x`).
* **Avatar**: Cycle colors using `Left/Right Arrows`.
* **Connect**: Press `Enter`.



### Input Schema

| Action | Input | Context |
| --- | --- | --- |
| **Locomotion** | `W`, `A`, `S`, `D` | Omnidirectional movement. |
| **Orientation** | `Mouse Cursor` | Character faces cursor vector. |
| **Interaction** | `E` | Gather resources / Interact. |
| **Combat** | `LMB` | Primary attack. |
| **Hotbar** | `1` - `4` | Equip items or weapons. |
| **Craft/Build** | `5` | Equip Fence. |
| **Placement** | `LMB` | Place structure (if `5` is active). |
| **Structure Rotate** | `RMB` | Rotate held structure. |
| **Crafting** | `2`, `3`, `4` | Click locked slot to craft (if resources available). |
| **Recipe Overlay** | `I` | Toggle HUD recipe list. |

---

## Visual Design

The rendering engine employs a functional, grid-based aesthetic designed for clarity:

* **Motion Clarity:** High framerate rendering ensures fluid tracking of fast-moving targets.
* **Visual Feedback:** Particle emission and sprite transformations provide immediate confirmation of gathering impacts and combat hits.
* **Dynamic UI:** Inventory management and crafting states are visualized in real-time, overlaying the viewport only when necessary.
