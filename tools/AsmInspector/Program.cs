using System;
using System.IO;
using System.Linq;
using System.Reflection;

if (args.Length == 0)
{
    Console.WriteLine("Usage: AsmInspector <path-to-assembly>");
    return 1;
}

var path = args[0];
if (!File.Exists(path))
{
    Console.WriteLine($"File not found: {path}");
    return 2;
}

var asm = Assembly.LoadFile(Path.GetFullPath(path));
Console.WriteLine($"Loaded: {asm.FullName}");

void DumpType(string name)
{
    var t = asm.GetTypes().FirstOrDefault(x => x.Name == name || x.FullName?.EndsWith("."+name) == true);
    if (t == null)
    {
        Console.WriteLine($"Type not found: {name}");
        return;
    }

    Console.WriteLine($"\n=== Type: {t.FullName} ===");
    Console.WriteLine("Fields:");
    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        Console.WriteLine($"  {f.FieldType.Name} {f.Name}");

    Console.WriteLine("Properties:");
    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");

    Console.WriteLine("Methods:");
    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
    {
        if (m.IsSpecialName) continue;
        var pars = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
        Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({pars})");
    }
}

var typesToDump = new[] { "CameraAim", "PhysGrabber", "PlayerAvatar", "VRCameraAim", "PhysGrabObject", "PhysGrabBeam", "PhysGrabCart" };
foreach (var n in typesToDump)
    DumpType(n);

return 0;