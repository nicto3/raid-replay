CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1); REF=$(ls -d /usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref/*/ref/net8.0)
P=/home/claude/RaidReplay/src/RaidReplay.Plugin; F=/home/claude/Fika-Plugin/References
dotnet $CSC -nologo -noconfig -nostdlib -target:library -out:chk.dll -langversion:latest -nowarn:CS0436,CS0108,CS8981,CS0649,CS0169 \
  $(for f in $REF/*.dll; do echo -n "-r:$f "; done) -r:UnityEngine.CoreModule.dll -r:Sirenix.Serialization.dll -r:$F/hollowed.dll -r:$F/Comfort.dll -r:/tmp/bie/BepInEx/core/BepInEx.dll -r:/tmp/bie/BepInEx/core/0Harmony.dll \
  /home/claude/RaidReplay/src/RaidReplay.Core/*.cs $P/EftBridge.cs $P/RaidRecorder.cs $P/WorldSync.cs $P/NativeSettingsHook.cs $P/RaidStartPatch.cs Shim.cs 2>&1 | grep -E "error" | sed -E 's#/home/claude/RaidReplay/src/RaidReplay.Plugin/##' | sort | uniq | cut -c1-330 | head -40
