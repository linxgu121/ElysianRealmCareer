# LuaCs Client Portrait Patch

This folder contains optional LuaCs/C# client-side code for Elysian Realm Career.

## What it does

`ElysianPortraitPlugin.cs` hooks `CharacterInfo.DrawIcon(...)` on the client and replaces the generated vanilla head portrait for characters whose job identifier is `realme`.

The default portrait is an ASCII-path copy of the original portrait:

```text
Assets/UI/elysia_portrait.png
```

Change `PortraitRelativePath` in `OptionalLuaCs/CSharp/ElysianRealm.ClientPortrait/ElysianPortraitPlugin.cs` if you want to use another image.

## Requirements

- Client-Side LuaCs installed and enabled.
- C# execution enabled in LuaCs.
- The mod root must contain `ModConfig.xml`. This repository already includes it.

Vanilla Barotrauma ignores `ModConfig.xml` because it is not referenced by `filelist.xml`, so the XML-only version of the mod remains usable.

## Loading format

LuaCs reads `%ModDir%/ModConfig.xml`. This mod uses script assembly loading:

```xml
<Assembly
  Name="ElysianRealm.ClientPortrait"
  File="%ModDir%/OptionalLuaCs/CSharp/ElysianRealm.ClientPortrait/ElysianPortraitPlugin.cs"
  IsScript="true"
  Target="Client"
  Platform="Any"
  UseInternalAccessName="true" />
```

When the patch draws successfully it returns a non-null value to LuaCs, which skips the original void `DrawIcon` call. Non-`realme` characters fall back to vanilla drawing.
