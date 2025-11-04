using System;
using System.IO;
using Mono.Cecil;
using System.Linq;

if (args.Length == 0)
{
    Console.WriteLine("Usage: AsmCecilInspector <path-to-assembly>");
    return 1;
}

var path = args[0];
if (!File.Exists(path))
{
    Console.WriteLine($"File not found: {path}");
    return 2;
}

var module = ModuleDefinition.ReadModule(path);
Console.WriteLine($"Loaded module: {module.Name}");

void DumpType(string name)
{
    var t = module.Types.FirstOrDefault(x => x.Name == name || (x.Namespace != null && x.FullName.EndsWith("." + name)));
    if (t == null)
    {
        Console.WriteLine($"Type not found: {name}");
        return;
    }

    Console.WriteLine($"\n=== Type: {t.FullName} ===");
    Console.WriteLine("Fields:");
    foreach (var f in t.Fields)
        Console.WriteLine($"  {f.FieldType.Name} {f.Name}");

    Console.WriteLine("Methods:");
    foreach (var m in t.Methods.Where(m => !m.IsGetter && !m.IsSetter))
    {
        var pars = string.Join(", ", m.Parameters.Select(p => p.ParameterType.Name + " " + p.Name));
        Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({pars})");
    }
}

var typesToDump = new[] { "CameraAim", "PhysGrabber", "PlayerAvatar", "VRCameraAim", "PhysGrabObject", "PhysGrabBeam", "PhysGrabCart" };
foreach (var n in typesToDump)
    DumpType(n);

return 0;