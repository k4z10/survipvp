using Raylib_cs;
using System.Numerics;

namespace GameClient;

public class Game
{
    private readonly NetworkClient _net;
    
    // Konfiguracja widoku
    private const float ViewWidthUnits = 20.0f; // Zoom (widzimy 20 jednostek świata)
    private const float PlayerRadius = 0.5f;    // Promień gracza w świecie
    private const int MapSize = 500;
    
    // Stan lokalny
    private float _localX = 250f;
    private float _localY = 250f;
    private const float Speed = 10.0f; // Prędkość poruszania

    public Game()
    {
        // 1. Inicjalizacja okna
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(1024, 768, "2D MMO Client");
        Raylib.SetTargetFPS(60);

        _net = new NetworkClient();
    }

    public async Task Run(string ip, int port)
    {
        // 2. Łączenie w tle (nie blokujemy renderowania przy starcie)
        Task connectionTask = _net.ConnectAsync(ip, port);

        // Główna pętla gry
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime(); // Delta Time w sekundach

            HandleInput(dt);
            Render();
        }

        Raylib.CloseWindow();
        await connectionTask; // Czekamy na czyste zamknięcie socketu (opcjonalne)
    }

    private void HandleInput(float dt)
    {
        // Wykrywanie Inputu
        Vector2 movement = Vector2.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.W)) movement.Y -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.S)) movement.Y += 1;
        if (Raylib.IsKeyDown(KeyboardKey.A)) movement.X -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.D)) movement.X += 1;

        // Normalizacja wektora (żeby ruch po skosie nie był szybszy)
        if (movement.LengthSquared() > 0)
        {
            movement = Vector2.Normalize(movement);
            
            float nextX = _localX + movement.X * Speed * dt;
            float nextY = _localY + movement.Y * Speed * dt;

            // Collision Check (Client Prediction)
            bool collision = false;
            var others = _net.GetInterpolatedState(0.1f); 
            // Note: reusing the same state as render might be slightly off due to interpolation time, 
            // but is good enough for basic prediction.Ideally we'd use latest known snapshot.
            
            foreach (var p in others.Values)
            {
                if (p.Id == _net.MyPlayerId) continue;
                
                float dx = nextX - p.X;
                float dy = nextY - p.Y;
                float distSq = dx*dx + dy*dy;
                if (distSq < (PlayerRadius * 2) * (PlayerRadius * 2))
                {
                    collision = true;
                    break;
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
    }

    private void Render()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(new Color(20, 20, 20, 255)); // Ciemnoszare tło

        // Obliczenia transformacji (World -> Screen)
        float screenW = Raylib.GetScreenWidth();
        float screenH = Raylib.GetScreenHeight();
        
        // Scale: ile pikseli na 1 jednostkę świata
        float scale = screenW / ViewWidthUnits; 
        
        float centerX = screenW / 2.0f;
        float centerY = screenH / 2.0f;

        // ---------------------------------------------------------
        // 1. Rysowanie Siatki (Grid) - kluczowe dla poczucia ruchu
        // ---------------------------------------------------------
        DrawGrid(scale, centerX, centerY, screenW, screenH);

        // ---------------------------------------------------------
        // 2. Rysowanie Innych Graczy (Remote)
        // ---------------------------------------------------------
        // ---------------------------------------------------------
        // 2. Rysowanie Innych Graczy (Remote)
        // ---------------------------------------------------------
        var snapshot = _net.GetInterpolatedState(0.1f); // 100ms opóźnienia interpolacji
        foreach (var player in snapshot.Values)
        {
            if (player.Id == _net.MyPlayerId) continue; // Pomijamy siebie

            // Frustum Culling (prymitywny)
            if (Math.Abs(player.X - _localX) > ViewWidthUnits) continue;
            if (Math.Abs(player.Y - _localY) > ViewWidthUnits) continue;

            // Konwersja World -> Screen
            float sx = (player.X - _localX) * scale + centerX;
            float sy = (player.Y - _localY) * scale + centerY;

            Raylib.DrawCircleV(new Vector2(sx, sy), PlayerRadius * scale, Color.Red);
            
            // Opcjonalnie: ID nad głową
            Raylib.DrawText(player.Id.ToString(), (int)sx - 5, (int)sy - 40, 20, Color.White);
        }

        // ---------------------------------------------------------
        // 3. Rysowanie Gracza Lokalnego (Zawsze środek)
        // ---------------------------------------------------------
        Raylib.DrawCircleV(new Vector2(centerX, centerY), PlayerRadius * scale, Color.SkyBlue);
        Raylib.DrawCircleLines((int)centerX, (int)centerY, PlayerRadius * scale, Color.White); // Obrys

        // ---------------------------------------------------------
        // 4. UI / Debug
        // ---------------------------------------------------------
        Raylib.DrawFPS(10, 10);
        Raylib.DrawText($"Pos: {_localX:F1}, {_localY:F1}", 10, 35, 20, Color.Green);

        Raylib.EndDrawing();
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