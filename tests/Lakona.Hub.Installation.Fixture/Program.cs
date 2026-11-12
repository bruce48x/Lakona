using System.Text.Json;

// A real child process for the installer's startup/rollback contract. Its entire
// deployment is copied into an isolated test installation, never a user profile.
var mode = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixture-mode.txt"));
if (mode == "exit") return 7;
var index = Array.IndexOf(args, "--hub-startup-token");
if (index < 0 || index + 1 >= args.Length) return 8;
var root = Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.Parent!.FullName;
var health = Path.Combine(root, "Lakona", "Hub", "installer", "health.json");
var confirmation = new { Version = "1.0.0", Token = mode == "wrong-token" ? "wrong" : args[index + 1] };
File.WriteAllText(health + ".tmp", JsonSerializer.Serialize(confirmation));
File.Move(health + ".tmp", health, true);
await Task.Delay(TimeSpan.FromSeconds(60));
return 0;
