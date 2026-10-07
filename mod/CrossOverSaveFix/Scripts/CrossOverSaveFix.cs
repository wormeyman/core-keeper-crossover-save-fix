using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Pug.Platform;
using PugMod;
using UnityEngine;

// Under Wine (CrossOver), Core Keeper can't save to a file that doesn't exist yet.
// The game writes a temp file and swaps it in for the old one, moving the old one to
// <name>.pugbackup. When the old file or its .pugbackup is missing, that swap fails
// with error code 0 ("Write failed: Success"), so new characters, worlds and map
// files are never saved, and Steam Cloud uploads stop at the first missing file.
//
// This mod runs just before each write and creates the target file and its
// .pugbackup empty when either is missing, so the swap has something to replace.
// It never touches a file that already exists. See README.md for how this was found.
public class CrossOverSaveFix : IMod
{
	public void EarlyInit() => Debug.Log("[CrossOverSaveFix] loaded");

	// Report whether the hook really got attached, so a silent failure shows up
	// in Player.log at startup instead of as a lost save later.
	public void Init()
	{
		foreach (var method in StandaloneFilesystemWritePatch.TargetMethods())
		{
			var attached = false;
			var prefixes = Harmony.GetPatchInfo(method)?.Prefixes;
			if (prefixes != null)
			{
				foreach (var patch in prefixes)
				{
					attached |= patch.PatchMethod.DeclaringType == typeof(StandaloneFilesystemWritePatch);
				}
			}
			Debug.Log($"[CrossOverSaveFix] {method.DeclaringType.Name}.{method.Name}({method.GetParameters().Length} args): " +
				(attached ? "hook attached" : "HOOK NOT ATTACHED - saves are not protected"));
		}

		RemoveOwnLoaderCopy();
	}

	// The mod loader copies this mod's scripts to a temp folder and compiles them
	// from there. On the next launch it deletes that copy first, and under CrossOver
	// that delete failed (the folder looked non-empty right after its file was
	// removed), so the loader skipped the mod. The scripts are compiled by the time
	// Init runs, so remove the copy now and let the next launch start clean.
	static void RemoveOwnLoaderCopy()
	{
		var copy = Path.Combine(Path.GetTempPath(), Application.companyName, Application.productName,
			"ModLoader", "CrossOverSaveFix");
		try
		{
			if (!Directory.Exists(copy))
			{
				Debug.Log($"[CrossOverSaveFix] no loader copy at {copy}");
				return;
			}
			foreach (var file in Directory.GetFiles(copy, "*", SearchOption.AllDirectories))
			{
				File.Delete(file);
			}
			var dirs = Directory.GetDirectories(copy, "*", SearchOption.AllDirectories);
			Array.Sort(dirs, (a, b) => b.Length.CompareTo(a.Length)); // deepest first
			foreach (var dir in dirs)
			{
				Directory.Delete(dir);
			}
			Directory.Delete(copy);
			Debug.Log($"[CrossOverSaveFix] removed loader copy at {copy}");
		}
		catch (Exception e)
		{
			// Files gone but folders left is still enough for the next launch to try;
			// report what happened either way.
			Debug.LogWarning($"[CrossOverSaveFix] could not fully remove loader copy at {copy}: {e.Message}");
		}
	}

	public void Shutdown() { }
	public void ModObjectLoaded(UnityEngine.Object obj) { }
	public bool CanBeUnloaded() => false;
	public void Update() { }
}

[HarmonyPatch]
public static class StandaloneFilesystemWritePatch
{
	// Both Write overloads: (path, data) and (name, path, data). The two-argument
	// one just calls the three-argument one.
	[HarmonyTargetMethods]
	public static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(StandaloneFilesystem), nameof(StandaloneFilesystem.Write),
			new[] { typeof(string), typeof(byte[]) });
		yield return AccessTools.Method(typeof(StandaloneFilesystem), nameof(StandaloneFilesystem.Write),
			new[] { typeof(string), typeof(string), typeof(byte[]) });
	}

	// Set to true to log the state of <file>, .pugbackup and .pugtmp before and after
	// every write. This is how the File.Exists problem (see ReallyExists) was found.
	static readonly bool Diagnostics = false;
	static int calls;

	// The game's Write deletes <file>.pugbackup if File.Exists says it is there,
	// writes <file>.pugtmp, then calls File.Replace(tmp, file, backup) if File.Exists
	// says <file> is there, or File.Move(tmp, file) if not. Under CrossOver those
	// checks can report missing files as present, so the game takes the wrong branch
	// and the write fails. Making sure both files really exist keeps it on the path
	// that works.

	[HarmonyPrefix]
	public static void Prefix(StandaloneFilesystem __instance, string path, out int __state)
	{
		__state = System.Threading.Interlocked.Increment(ref calls);
		try
		{
			var full = Path.IsPathRooted(path) ? path : __instance.Rel2Abs(path);
			if (Diagnostics)
			{
				Debug.LogWarning($"[CrossOverSaveFix] #{__state} before {path}: {Describe(full)}");
			}
			EnsureExists(full);
			EnsureExists(full + ".pugbackup");
		}
		catch (Exception e)
		{
			// Never block the game's own write; just report.
			Debug.LogWarning($"[CrossOverSaveFix] could not prepare {path}: {e.Message}");
		}
	}

	[HarmonyPostfix]
	public static void Postfix(StandaloneFilesystem __instance, string path, int __state)
	{
		if (!Diagnostics)
		{
			return;
		}
		try
		{
			var full = Path.IsPathRooted(path) ? path : __instance.Rel2Abs(path);
			Debug.LogWarning($"[CrossOverSaveFix] #{__state} after  {path}: {Describe(full)}");
		}
		catch (Exception e)
		{
			Debug.LogWarning($"[CrossOverSaveFix] could not describe {path}: {e.Message}");
		}
	}

	static string Describe(string full)
	{
		// Size from listing the folder; File.Exists in brackets when it disagrees.
		string One(string file)
		{
			var listed = ReallyExists(file);
			var said = File.Exists(file);
			var size = listed ? new FileInfo(file).Length.ToString() : "-";
			return said == listed ? size : $"{size}[File.Exists={said}]";
		}
		return $"file={One(full)} backup={One(full + ".pugbackup")} tmp={One(full + ".pugtmp")}";
	}

	// Under CrossOver, File.Exists reported .pugtmp and .pugbackup files in the save
	// folder as present (0 bytes) when they were not on disk. A made-up name in the
	// temp folder was reported correctly, so it is not every path. Listing the folder
	// gives the real answer.
	public static bool ReallyExists(string file)
	{
		var dir = Path.GetDirectoryName(file);
		var name = Path.GetFileName(file);
		try
		{
			foreach (var entry in Directory.EnumerateFiles(dir))
			{
				if (string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		catch (DirectoryNotFoundException)
		{
		}
		return false;
	}

	static void EnsureExists(string file)
	{
		if (ReallyExists(file))
		{
			return;
		}
		Directory.CreateDirectory(Path.GetDirectoryName(file));
		File.WriteAllBytes(file, Array.Empty<byte>());
		Debug.LogWarning($"[CrossOverSaveFix] created empty {file} so the save can replace it");
	}
}
