using Raylib_cs;
using System.Numerics;
using GameShared;

namespace GameClient;

public class Game
{
    private readonly NetworkClient _net;

    // widok
    private const float ViewWidthUnits = 20.0f; // Zoom
    private const float PlayerRadius = 0.5f;
    private const int MapSize = 500;

    // stan lokalny
    private float _localX = 250f;
    private float _localY = 250f;
    private const float Speed = 10.0f;
    private bool _buildMode = false;
    private bool _showRecipes = false;
    private float _ghostRotation = 0f;
    
    private enum GameState
    {
        Startup,
        Playing,
        Death
    }

    private GameState _gameState = GameState.Startup;

    // Startup
    private string _nickname = "Player";
    private int _selectedColorIndex = 0;

    private readonly Color[] _availableColors =
    {
        Color.SkyBlue, Color.Red, Color.Green, Color.Orange, Color.Purple, Color.Pink, Color.White, Color.Black
    };

    private readonly string[] _colorNames =
    {
        "Sky Blue", "Red", "Green", "Orange", "Purple", "Pink", "White", "Black"
    };

    // Connection
    private string _serverIp = "127.0.0.1";
    private bool _editingIp = false; // false = Nickname, true = IP
    private bool _isConnecting = false;
    private Task _connectionTask;

    // Death
    private float _deathTimer = 0f;

    public Game()
    {
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(1024, 768, "2D MMO Client");
        Raylib.SetTargetFPS(60);

        _net = new NetworkClient();
        _net.OnDeath += OnDeath;
    }

    private void OnDeath(int respawnTimeMs)
    {
        _gameState = GameState.Death;
        _deathTimer = respawnTimeMs / 1000.0f;
    }

    public async Task Run(int port)
    {
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime(); // Delta Time w sekundach

            switch (_gameState)
            {
                case GameState.Startup:
                    UpdateStartup(port);
                    break;
                case GameState.Playing:
                    HandleInput(dt);
                    break;
                case GameState.Death:
                    UpdateDeath(dt);
                    break;
            }

            Render();
        }

        Raylib.CloseWindow();
        Raylib.CloseWindow();
        if (_connectionTask != null) await _connectionTask;
    }

    private void HandleInput(float dt)
    {
        Vector2 movement = Vector2.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.W)) movement.Y -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.S)) movement.Y += 1;
        if (Raylib.IsKeyDown(KeyboardKey.A)) movement.X -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.D)) movement.X += 1;
        
        if (movement.LengthSquared() > 0)
        {
            movement = Vector2.Normalize(movement); // normalizacja, żeby po skosie nie było szybciej

            float nextX = _localX + movement.X * Speed * dt;
            float nextY = _localY + movement.Y * Speed * dt;

            // sprawdzanie kolizji (client side)
            bool collision = false;
            var others = _net.GetInterpolatedState(0.1f);

            foreach (var p in others.Values)
            {
                if (p.Id == _net.MyPlayerId) continue;

                float dx = nextX - p.X;
                float dy = nextY - p.Y;
                float distSq = dx * dx + dy * dy;
                if (distSq < (PlayerRadius * 2) * (PlayerRadius * 2))
                {
                    collision = true;
                    break;
                }
            }
            
            if (!collision)
            {
                foreach (var s in _net.Structures.Values)
                {
                    float sw = (s.Rotation == 0) ? 3.0f : 1.0f;
                    float sh = (s.Rotation == 0) ? 1.0f : 3.0f;
                    float hw = sw / 2.0f;
                    float hh = sh / 2.0f;

                    float closeX = Math.Clamp(nextX, s.X - hw, s.X + hw);
                    float closeY = Math.Clamp(nextY, s.Y - hh, s.Y + hh);

                    float dx = nextX - closeX;
                    float dy = nextY - closeY;
                    float distSq = dx * dx + dy * dy;

                    if (distSq < PlayerRadius * PlayerRadius)
                    {
                        collision = true;

                        // push-out logic (taka sama jak w serwerze)
                        float dist = MathF.Sqrt(distSq);
                        float nx, ny, overlap;
                        if (dist < 0.0001f)
                        {
                            // Inside
                            float dLeft = MathF.Abs(nextX - (s.X - hw));
                            float dRight = MathF.Abs(nextX - (s.X + hw));
                            float dTop = MathF.Abs(nextY - (s.Y - hh));
                            float dBottom = MathF.Abs(nextY - (s.Y + hh));
                            float minD = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));

                            if (Math.Abs(minD - dLeft) < 0.001f)
                            {
                                nx = -1;
                                ny = 0;
                                overlap = dLeft + PlayerRadius;
                            }
                            else if (Math.Abs(minD - dRight) < 0.001f)
                            {
                                nx = 1;
                                ny = 0;
                                overlap = dRight + PlayerRadius;
                            }
                            else if (Math.Abs(minD - dTop) < 0.001f)
                            {
                                nx = 0;
                                ny = -1;
                                overlap = dTop + PlayerRadius;
                            }
                            else
                            {
                                nx = 0;
                                ny = 1;
                                overlap = dBottom + PlayerRadius;
                            }
                        }
                        else
                        {
                            overlap = PlayerRadius - dist;
                            nx = dx / dist;
                            ny = dy / dist;
                        }
                        
                        nextX += nx * overlap;
                        nextY += ny * overlap;
                        collision = false;
                    }
                }
            }

            if (!collision)
            {
                _localX = nextX;
                _localY = nextY;
            }

            // Clamp do granic mapy
            _localX = Math.Clamp(_localX, 0, MapSize);
            _localY = Math.Clamp(_localY, 0, MapSize);

            // Wysyłamy pozycję do serwera
            _net.SendPosition(_localX, _localY);
        }

        // toggle info
        if (Raylib.IsKeyPressed(KeyboardKey.I))
        {
            _showRecipes = !_showRecipes;
        }
        
        // zbieranie surowców
        if (Raylib.IsKeyPressed(KeyboardKey.E))
        {
            int? nearestId = null;
            float minDistSq = float.MaxValue;
            const float GatherRangeSq = 3.0f * 3.0f;

            if (_net.Resources != null)
            {
                foreach (var res in _net.Resources.Values)
                {
                    if (!res.IsActive) continue;

                    float dx = res.X - _localX;
                    float dy = res.Y - _localY;
                    float distSq = dx * dx + dy * dy;

                    if (distSq < GatherRangeSq && distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        nearestId = res.Id;
                    }
                }
            }

            if (nearestId.HasValue)
            {
                _net.SendGather(nearestId.Value);
                Console.WriteLine($"Try gather {nearestId.Value}");
            }
        }
        
        if (Raylib.IsKeyPressed(KeyboardKey.One))
        {
            HandleWeaponInput(WeaponType.None);
            _buildMode = false;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Two))
        {
            HandleWeaponInput(WeaponType.WoodSword);
            _buildMode = false;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Three))
        {
            HandleWeaponInput(WeaponType.StoneSword);
            _buildMode = false;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Four))
        {
            HandleWeaponInput(WeaponType.GoldSword);
            _buildMode = false;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Five))
        {
            if (_buildMode)
            {
                _net.SendCraft(WeaponType.Fence);
                Console.WriteLine("Request Craft Fence (Extra)");
            }
            else
            {
                if (_net.Inventory.TryGetValue(ResourceType.Fence, out int count) && count > 0)
                {
                    _buildMode = true;
                    Console.WriteLine("Build Mode: Fence");
                }
                else
                {
                    _net.SendCraft(WeaponType.Fence);
                    Console.WriteLine("Request Craft Fence");
                }
            }
        }

        Vector2 mousePos = Raylib.GetMousePosition();
        float centerX = Raylib.GetScreenWidth() / 2.0f;
        float centerY = Raylib.GetScreenHeight() / 2.0f;

        if (_buildMode)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Right))
            {
                _ghostRotation = (_ghostRotation == 0) ? MathF.PI / 2 : 0;
            }

            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                // Calculate World Mouse Pos
                // Screen = (World - Local) * scale + Center
                // World - Local = (Screen - Center) / scale
                // World = (Screen - Center) / scale + Local

                float screenW = Raylib.GetScreenWidth();
                float scale = screenW / ViewWidthUnits;

                float mx = (mousePos.X - centerX) / scale + _localX;
                float my = (mousePos.Y - centerY) / scale + _localY;

                if (_net.Inventory.TryGetValue(ResourceType.Fence, out int fc) && fc > 0)
                {
                    _net.SendBuild(StructureType.Wall, mx, my, _ghostRotation);
                }
                else
                {
                    Console.WriteLine("Cannot build: No fences.");
                }
            }
        }
        else
        {
            // Rotation
            float mouseDx = mousePos.X - centerX;
            float mouseDy = mousePos.Y - centerY;
            float rotation = MathF.Atan2(mouseDy, mouseDx);
            _net.SendRotate(rotation);

            // Attack
            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _net.SendAttack();
            }
        }
    }

    private void HandleWeaponInput(WeaponType weapon)
    {
        if (weapon == WeaponType.None)
        {
            _net.SendEquip(WeaponType.None);
            Console.WriteLine("Equipped Hand");
            return;
        }

        if (_net.UnlockedWeapons.Contains(weapon))
        {
            _net.SendEquip(weapon);
            Console.WriteLine($"Request Equip {weapon}");
        }
        else
        {
            _net.SendCraft(weapon);
            Console.WriteLine($"Request Craft {weapon}");
        }
    }

    private void Render()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(20, 20, 20, 255));

        switch (_gameState)
        {
            case GameState.Startup:
                DrawStartup();
                break;
            case GameState.Playing:
                DrawGame();
                break;
            case GameState.Death:
                DrawGame();
                DrawDeathOverlay();
                break;
        }

        Raylib.EndDrawing();
    }

    private void UpdateStartup(int port)
    {
        if (_isConnecting)
        {
            if (_net.IsConnected)
            {
                _isConnecting = false;

                // Send Join
                uint c = (uint)Raylib.ColorToInt(_availableColors[_selectedColorIndex]);
                _net.SendJoinRequest(_nickname, c);
                _gameState = GameState.Playing;
            }

            return;
        }

        // Tab Switch
        if (Raylib.IsKeyPressed(KeyboardKey.Tab))
        {
            _editingIp = !_editingIp;
        }

        // Input
        int key = Raylib.GetCharPressed();
        while (key > 0)
        {
            if ((key >= 32) && (key <= 125))
            {
                if (!_editingIp)
                {
                    if (_nickname.Length < 16) _nickname += (char)key;
                }
                else
                {
                    if (_serverIp.Length < 20) _serverIp += (char)key;
                }
            }

            key = Raylib.GetCharPressed();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Backspace))
        {
            if (!_editingIp)
            {
                if (_nickname.Length > 0) _nickname = _nickname.Substring(0, _nickname.Length - 1);
            }
            else
            {
                if (_serverIp.Length > 0) _serverIp = _serverIp.Substring(0, _serverIp.Length - 1);
            }
        }

        // Color Selection
        if (Raylib.IsKeyPressed(KeyboardKey.Left)) _selectedColorIndex--;
        if (Raylib.IsKeyPressed(KeyboardKey.Right)) _selectedColorIndex++;
        if (_selectedColorIndex < 0) _selectedColorIndex = _availableColors.Length - 1;
        if (_selectedColorIndex >= _availableColors.Length) _selectedColorIndex = 0;

        // Connect & Play
        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            if (!string.IsNullOrWhiteSpace(_nickname) && !string.IsNullOrWhiteSpace(_serverIp))
            {
                _isConnecting = true;
                _connectionTask = _net.ConnectAsync(_serverIp, port);
            }
        }
    }

    private void DrawStartup()
    {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();
        int cx = sw / 2;
        int cy = sh / 2;

        Raylib.DrawText("survipvp", cx - 150, cy - 200, 60, Color.Red);

        // Nickname
        Color nickColor = (!_editingIp) ? Color.Yellow : Color.Gray;
        Raylib.DrawText("Nickname:", cx - 100, cy - 80, 20, nickColor);
        Raylib.DrawRectangle(cx - 100, cy - 50, 200, 30, Color.LightGray);
        Raylib.DrawRectangleLines(cx - 100, cy - 50, 200, 30, (!_editingIp) ? Color.Yellow : Color.White);
        Raylib.DrawText(_nickname, cx - 90, cy - 45, 20, Color.Black);

        // IP Address
        Color ipColor = (_editingIp) ? Color.Yellow : Color.Gray;
        Raylib.DrawText("Server IP (Tab):", cx - 100, cy - 10, 20, ipColor);
        Raylib.DrawRectangle(cx - 100, cy + 20, 200, 30, Color.LightGray);
        Raylib.DrawRectangleLines(cx - 100, cy + 20, 200, 30, (_editingIp) ? Color.Yellow : Color.White);
        Raylib.DrawText(_serverIp, cx - 90, cy + 25, 20, Color.Black);

        // Color Picker
        Raylib.DrawText("Choose Color (< >):", cx - 100, cy + 70, 20, Color.Gray);
        Color c = _availableColors[_selectedColorIndex];
        string cName = _colorNames[_selectedColorIndex];

        int nameWidth = Raylib.MeasureText(cName, 30);
        Raylib.DrawText(cName, cx - nameWidth / 2, cy + 100, 30, c);

        // Status / Play
        if (_isConnecting)
        {
            Raylib.DrawText("Connecting...", cx - 60, cy + 150, 20, Color.Yellow);
        }
        else
        {
            Raylib.DrawText("Press ENTER to Join", cx - 100, cy + 150, 20, Color.Green);
        }
    }

    private void UpdateDeath(float dt)
    {
        _deathTimer -= dt;
        if (_deathTimer <= 0)
        {
            _gameState = GameState.Startup;
            _localX = 250;
            _localY = 250;
        }
    }

    private void DrawDeathOverlay()
    {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();

        Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 150));

        string msg = "YOU DIED";
        Raylib.DrawText(msg, sw / 2 - Raylib.MeasureText(msg, 60) / 2, sh / 2 - 50, 60, Color.Red);

        string sub = $"Respawn in {_deathTimer:F1}s";
        Raylib.DrawText(sub, sw / 2 - Raylib.MeasureText(sub, 30) / 2, sh / 2 + 20, 30, Color.White);
    }

    private void DrawGame()
    {
        float screenW = Raylib.GetScreenWidth();
        float screenH = Raylib.GetScreenHeight();

        float scale = screenW / ViewWidthUnits;

        float centerX = screenW / 2.0f;
        float centerY = screenH / 2.0f;

        DrawGrid(scale, centerX, centerY, screenW, screenH);

        // rysowanie innych graczy
        var snapshot = _net.GetInterpolatedState(0.1f); // 100ms opóźnienia interpolacji
        foreach (var player in snapshot.Values)
        {
            if (player.Id == _net.MyPlayerId) continue;

            // rysowanie tylko tego co widać
            if (Math.Abs(player.X - _localX) > ViewWidthUnits) continue;
            if (Math.Abs(player.Y - _localY) > ViewWidthUnits) continue;

            // konwersja world -> screen
            float sx = (player.X - _localX) * scale + centerX;
            float sy = (player.Y - _localY) * scale + centerY;

            Raylib.DrawCircleV(new Vector2(sx, sy), PlayerRadius * scale, Raylib.GetColor(player.Color));
            DrawWeapon(new Vector2(sx, sy), player.CurrentWeapon, scale, player.Rotation);
            DrawHealthBar(sx, sy, player.HP, scale);

            if (!string.IsNullOrEmpty(player.Nickname))
            {
                int width = Raylib.MeasureText(player.Nickname, 20);
                Raylib.DrawText(player.Nickname, (int)sx - width / 2, (int)sy - 60, 20, Color.White);
            }

            // ID nad głową
            // Raylib.DrawText(player.Id.ToString(), (int)sx - 5, (int)sy - 40, 20, new Color(255, 255, 255, 100));
        }

        // rysowanie surowców
        if (_net.Resources != null)
        {
            const float ResourceSize = 0.8f;
            float rsHalf = ResourceSize / 2.0f;
            float rsScale = ResourceSize * scale;

            foreach (var res in _net.Resources.Values)
            {
                if (!res.IsActive) continue;

                if (Math.Abs(res.X - _localX) > ViewWidthUnits) continue;
                if (Math.Abs(res.Y - _localY) > ViewWidthUnits) continue;

                float sx = (res.X - _localX) * scale + centerX;
                float sy = (res.Y - _localY) * scale + centerY;

                Color color = res.Type switch
                {
                    ResourceType.Tree => Color.Brown,
                    ResourceType.Rock => Color.Gray,
                    ResourceType.GoldMine => Color.Yellow,
                    _ => Color.White
                };

                Raylib.DrawRectangleV(new Vector2(sx - rsScale / 2, sy - rsScale / 2), new Vector2(rsScale, rsScale),
                    color);
            }
        }

        // rysowanie struktur
        foreach (var structure in _net.Structures.Values)
        {
            if (Math.Abs(structure.X - _localX) > ViewWidthUnits) continue;
            if (Math.Abs(structure.Y - _localY) > ViewWidthUnits) continue;

            float sx = (structure.X - _localX) * scale + centerX;
            float sy = (structure.Y - _localY) * scale + centerY;

            float w = (structure.Rotation == 0) ? 3.0f : 1.0f;
            float h = (structure.Rotation == 0) ? 1.0f : 3.0f;
            float sw = w * scale;
            float sh = h * scale;

            Raylib.DrawRectangleV(new Vector2(sx - sw / 2, sy - sh / 2), new Vector2(sw, sh), Color.Brown);
            Raylib.DrawRectangleLines((int)(sx - sw / 2), (int)(sy - sh / 2), (int)sw, (int)sh, Color.Black);

            // healthbar jeśli straciła życie
            if (structure.HP < 100)
            {
                DrawHealthBar(sx, sy, structure.HP, scale);
            }
        }

        // render ghost-state struktury
        if (_buildMode)
        {
            Vector2 mousePos = Raylib.GetMousePosition();
            // Snap to grid? optional. For now free placement.
            DrawGhost(mousePos, scale, _ghostRotation);
        }

        // renderowanie gracza lokalnego
        Color localColor = Color.SkyBlue;
        if (snapshot.TryGetValue(_net.MyPlayerId, out var myStateInternal))
        {
            localColor = Raylib.GetColor(myStateInternal.Color);
        }

        Raylib.DrawCircleV(new Vector2(centerX, centerY), PlayerRadius * scale, localColor);
        Raylib.DrawCircleLines((int)centerX, (int)centerY, PlayerRadius * scale, Color.White); // Obrys

        // rysuj miecz
        Vector2 mousePosRender = Raylib.GetMousePosition();
        float drx = mousePosRender.X - centerX;
        float dry = mousePosRender.Y - centerY;
        float localRotation = MathF.Atan2(dry, drx);

        if (snapshot.TryGetValue(_net.MyPlayerId, out var myState))
        {
            DrawWeapon(new Vector2(centerX, centerY), myState.CurrentWeapon, scale, localRotation);
            DrawHealthBar(centerX, centerY, myState.HP, scale);
            if (!string.IsNullOrEmpty(myState.Nickname))
            {
                int width = Raylib.MeasureText(myState.Nickname, 20);
                Raylib.DrawText(myState.Nickname, (int)centerX - width / 2, (int)centerY - 60, 20, Color.Green);
            }
        }

        // UI
        Raylib.DrawFPS(10, 10);

        string status = _net.IsConnected ? "Connected" : "Disconnected";
        Color statusColor = _net.IsConnected ? Color.Green : Color.Red;
        Raylib.DrawText($"Status: {status}", 10, 35, 20, statusColor);

        Raylib.DrawText($"Pos: {_localX:F1}, {_localY:F1}", 10, 60, 20, Color.Green);

        // HUD
        DrawResourceHUD();
        int activeSlot = 0;
        if (_buildMode) activeSlot = 4;
        else if (myState.CurrentWeapon == WeaponType.WoodSword) activeSlot = 1;
        else if (myState.CurrentWeapon == WeaponType.StoneSword) activeSlot = 2;
        else if (myState.CurrentWeapon == WeaponType.GoldSword) activeSlot = 3;

        DrawHotbar(activeSlot, screenW, screenH);

        if (_showRecipes)
        {
            DrawRecipeOverlay(screenW);
        }
    }

    private void DrawRecipeOverlay(float screenW)
    {
        int startX = (int)screenW - 250;
        int startY = 10;
        int width = 240;
        int height = 150;

        Raylib.DrawRectangle(startX, startY, width, height, new Color(0, 0, 0, 200));
        Raylib.DrawRectangleLines(startX, startY, width, height, Color.White);

        Raylib.DrawText("Recipes (Press I to close):", startX + 10, startY + 10, 10, Color.White);

        int y = startY + 30;
        foreach (var recipe in CraftingRecipes.Recipes)
        {
            string line = $"{recipe.Key}: {CraftingRecipes.GetRecipeString(recipe.Key)}";
            Raylib.DrawText(line, startX + 10, y, 10, Color.LightGray);
            y += 20;
        }
    }

    private void DrawResourceHUD()
    {
        int startX = 10;
        int startY = 85;

        int wood = 0, stone = 0, gold = 0;
        if (_net.Inventory.TryGetValue(ResourceType.Tree, out int w)) wood = w;
        if (_net.Inventory.TryGetValue(ResourceType.Rock, out int s)) stone = s;
        if (_net.Inventory.TryGetValue(ResourceType.GoldMine, out int g)) gold = g;

        Raylib.DrawText($"Wood: {wood}", startX, startY, 20, Color.Brown);
        Raylib.DrawText($"Stone: {stone}", startX, startY + 25, 20, Color.Gray);
        Raylib.DrawText($"Gold: {gold}", startX, startY + 50, 20, Color.Gold);
    }

    private void DrawHotbar(int activeSlot, float screenW, float screenH)
    {
        const int SlotSize = 50;
        const int Padding = 10;
        const int SlotCount = 5;

        float totalWidth = (SlotCount * SlotSize) + ((SlotCount - 1) * Padding);
        float startX = (screenW - totalWidth) / 2.0f;
        float startY = screenH - SlotSize - 20;

        for (int i = 0; i < SlotCount; i++)
        {
            float x = startX + i * (SlotSize + Padding);

            Color bgColor = (i == activeSlot) ? Color.Green : new Color(50, 50, 50, 200);
            Raylib.DrawRectangle((int)x, (int)startY, SlotSize, SlotSize, bgColor);
            Raylib.DrawRectangleLines((int)x, (int)startY, SlotSize, SlotSize, Color.White);

            Raylib.DrawText((i + 1).ToString(), (int)x + 2, (int)startY + 2, 10, Color.White);

            string label = "";
            Color iconColor = Color.White;

            switch (i)
            {
                case 0: label = "Hand"; break;
                case 1:
                    label = "Wood";
                    iconColor = Color.Brown;
                    break;
                case 2:
                    label = "Stone";
                    iconColor = Color.Gray;
                    break;
                case 3:
                    label = "Gold";
                    iconColor = Color.Yellow;
                    break;
                case 4:
                    label = "Fence";
                    iconColor = Color.Brown;

                    if (_net.Inventory.TryGetValue(ResourceType.Fence, out int fc))
                    {
                        Raylib.DrawText(fc.ToString(), (int)x + 20, (int)startY + 30, 15, Color.White);
                    }
                    else
                    {
                        Raylib.DrawText("0", (int)x + 20, (int)startY + 30, 15, Color.Gray);
                    }

                    break;
            }
            
            Raylib.DrawText(label, (int)x + 5, (int)startY + 15, 10, iconColor);
            if (i >= 1 && i <= 3)
            {
                WeaponType wt = i == 1 ? WeaponType.WoodSword : (i == 2 ? WeaponType.StoneSword : WeaponType.GoldSword);
                if (!_net.UnlockedWeapons.Contains(wt))
                {
                    Raylib.DrawText("LOCKED", (int)x + 2, (int)startY + 35, 8, Color.Red);
                }
            }
        }
    }

    private void DrawGhost(Vector2 mousePos, float scale, float rotation)
    {
        float w = (rotation == 0) ? 3.0f : 1.0f;
        float h = (rotation == 0) ? 1.0f : 3.0f;
        float sw = w * scale;
        float sh = h * scale;

        Raylib.DrawRectangleV(new Vector2(mousePos.X - sw / 2, mousePos.Y - sh / 2), new Vector2(sw, sh),
            new Color(139, 69, 19, 100)); // Semi-transparent brown
        Raylib.DrawRectangleLines((int)(mousePos.X - sw / 2), (int)(mousePos.Y - sh / 2), (int)sw, (int)sh,
            Color.White);
    }

    private void DrawWeapon(Vector2 center, WeaponType weapon, float scale, float rotation)
    {
        if (weapon == WeaponType.None) return;

        Color color = weapon switch
        {
            WeaponType.WoodSword => Color.Brown,
            WeaponType.StoneSword => Color.Gray,
            WeaponType.GoldSword => Color.Yellow,
            _ => Color.White
        };
        
        float w = 1.2f * scale;
        float h = 0.15f * scale;
        
        Raylib.DrawRectanglePro(
            new Rectangle(center.X, center.Y, w, h),
            new Vector2(0, h / 2),
            rotation * (180.0f / MathF.PI),
            color
        );
    }

    private void DrawHealthBar(float x, float y, int hp, float scale)
    {
        float w = 1.5f * scale;
        float h = 0.2f * scale;
        float yDist = 0.8f * scale;

        float px = x - w / 2;
        float py = y - yDist;

        Raylib.DrawRectangleV(new Vector2(px, py), new Vector2(w, h), Color.Red);
        float hpPct = Math.Clamp(hp, 0, 100) / 100.0f;
        Raylib.DrawRectangleV(new Vector2(px, py), new Vector2(w * hpPct, h), Color.Green);
    }

    // Pomocnicza metoda do rysowania siatki nieskończonej
    private void DrawGrid(float scale, float centerX, float centerY, float sw, float sh)
    {
        float gridSize = 1.0f; // Grid co 1 jednostkę świata
        Color gridColor = new Color(50, 50, 50, 255);

        // Offset przesunięcia siatki wynikający z pozycji gracza (modulo)
        float modX = (_localX % gridSize) * scale;
        float modY = (_localY % gridSize) * scale;

        // Rysuj linie pionowe
        for (float x = centerX - modX - sw; x < sw; x += gridSize * scale)
        {
            // Optymalizacja: rysuj tylko to co widać na ekranie
            if (x < -50 || x > sw + 50) continue;
            Raylib.DrawLineV(new Vector2(x, 0), new Vector2(x, sh), gridColor);
        }

        // Rysuj linie poziome
        for (float y = centerY - modY - sh; y < sh; y += gridSize * scale)
        {
            if (y < -50 || y > sh + 50) continue;
            Raylib.DrawLineV(new Vector2(0, y), new Vector2(sw, y), gridColor);
        }

        // Rysuj granice mapy (World Borders)
        // Lewa krawędź (x=0)
        if (_localX < ViewWidthUnits)
        {
            float screenX0 = (0 - _localX) * scale + centerX;
            Raylib.DrawLineEx(new Vector2(screenX0, 0), new Vector2(screenX0, sh), 5, Color.Yellow);
        }

        // Górna krawędź (y=0)
        if (_localY < ViewWidthUnits)
        {
            float screenY0 = (0 - _localY) * scale + centerY;
            Raylib.DrawLineEx(new Vector2(0, screenY0), new Vector2(sw, screenY0), 5, Color.Yellow);
        }
    }
}