# Reference Libraries

This directory holds the proprietary RimWorld and Unity engine assemblies required to compile the mod across multiple game versions.

> **Note:** These binaries are proprietary to Ludeon Studios / Unity Technologies and are ***deliberately excluded*** from Git tracking via `.gitignore`.

---

### Required Files

For each target version (`Libraries/1.5/` and `Libraries/1.6/`), you need the following assemblies:

* `Assembly-CSharp.dll`
* `UnityEngine.dll`
* `UnityEngine.CoreModule.dll`
* `UnityEngine.IMGUIModule.dll`
* `UnityEngine.TextRenderingModule.dll`
* `0Harmony.dll`

---

### How to Acquire the Assemblies

#### 1. Engine & Game DLLs
You can find `Assembly-CSharp.dll` and the `UnityEngine*.dll` files in your RimWorld installation:
* **Path:** `<SteamLibrary>/steamapps/common/RimWorld/RimWorldWin64_Data/Managed/`

#### 2. Harmony DLL
You can grab `0Harmony.dll` from the official Harmony workshop mod folder:
* **Path:** `<SteamLibrary>/steamapps/workshop/content/294100/2009463077/Current/Assemblies/0Harmony.dll`
* (Or download it from Andreas Pardeike's official [Harmony GitHub Releases](https://github.com/pardeike/Harmony/releases)).

---

### Sourcing Multiple RimWorld Versions via Steam

If your game is currently on version 1.6, you can easily download 1.5's files using Steam's branch selector:

1. In Steam, right-click **RimWorld** in your Library and select **Properties...**
2. Select the **Game Versions & Betas** tab on the left.
3. Select **Default Public Version** for latest current release, or select an alternative version, ie. `version-1.5`.
4. Let Steam download the files, then copy the required DLLs into the proper `Libraries/<version>/` directory.