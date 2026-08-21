using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RhythKit.Installer;

public static class RhythiaPatcher
{
    public static string Patch(string gameDirectory)
    {
        var assemblyPath = FindRhythiaAssembly(gameDirectory);
        var backupPath = assemblyPath + ".rhythkit-backup";
        if (!File.Exists(backupPath)) File.Copy(assemblyPath, backupPath);

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadWrite = false });
        var type = assembly.MainModule.Types.FirstOrDefault(x => x.FullName == "Rhythia");
        var method = type?.Methods.FirstOrDefault(x => x.Name == "_Ready");
        if (method == null) throw new InvalidOperationException("Could not find Rhythia._Ready in Rhythia.dll.");

        var bootstrap = FindBootstrap(assembly.MainModule);
        if (method.Body.Instructions.Any(x => x.OpCode == OpCodes.Call && x.Operand is MethodReference reference && reference.FullName == bootstrap.FullName)) return assemblyPath;

        method.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Call, bootstrap));
        var tempPath = assemblyPath + ".rhythkit.tmp";
        assembly.Write(tempPath);
        File.Move(tempPath, assemblyPath, true);
        return assemblyPath;
    }

    public static bool IsPatched(string assemblyPath)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { ReadWrite = false });
        var method = assembly.MainModule.Types.FirstOrDefault(x => x.FullName == "Rhythia")?.Methods.FirstOrDefault(x => x.Name == "_Ready");
        return method?.Body.Instructions.Any(x => x.Operand is MethodReference reference && reference.DeclaringType.FullName == "RhythKit.RhythKitBootstrap" && reference.Name == "Initialize") == true;
    }

    public static string FindRhythiaAssembly(string gameDirectory)
    {
        var direct = Path.Combine(gameDirectory, "Rhythia.dll");
        if (File.Exists(direct)) return direct;
        var matches = Directory.EnumerateFiles(gameDirectory, "Rhythia.dll", SearchOption.AllDirectories).ToArray();
        return matches.FirstOrDefault() ?? throw new FileNotFoundException("Rhythia.dll was not found in the selected game directory.");
    }

    private static MethodReference FindBootstrap(ModuleDefinition module)
    {
        var type = module.Types.FirstOrDefault(x => x.FullName == "RhythKit.RhythKitBootstrap");
        var method = type?.Methods.FirstOrDefault(x => x.Name == "Initialize" && !x.HasParameters);
        if (method == null) throw new InvalidOperationException("Rhythia.dll does not contain RhythKit.RhythKitBootstrap.Initialize. The selected Rhythia build is not compatible with this installer.");
        return module.ImportReference(method);
    }
}
