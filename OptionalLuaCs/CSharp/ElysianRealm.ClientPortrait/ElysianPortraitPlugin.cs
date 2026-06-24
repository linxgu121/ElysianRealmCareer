using System;
using System.Collections.Generic;
using System.Reflection;
using Barotrauma;
using Barotrauma.LuaCs;
using Barotrauma.LuaCs.Compatibility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.ElysianRealm;

public sealed class ElysianPortraitPlugin : IAssemblyPlugin
{
    private const string PatchIdentifier = "elysianrealm.clientportrait.characterinfo.drawicon";
    private const string TargetJobIdentifier = "realme";
    private const string PortraitRelativePath = "Assets/UI/elysia_portrait.png";

    private static ContentPackage ownerPackage;
    private static Sprite portraitSprite;
    private static string portraitFullPath;
    private static bool portraitLoadFailed;

    public void PreInitPatching()
    {
        LuaCsSetup.Instance.PluginManagementService.TryGetPackageForPlugin<ElysianPortraitPlugin>(out ownerPackage);

        MethodInfo drawIcon = typeof(CharacterInfo).GetMethod(
            "DrawIcon",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(SpriteBatch), typeof(Vector2), typeof(Vector2), typeof(bool) },
            modifiers: null);

        if (drawIcon == null)
        {
            LuaCsSetup.Instance.Logger.LogWarning("[ElysianRealm] CharacterInfo.DrawIcon was not found; portrait patch disabled.");
            return;
        }

        LuaCsSetup.Instance.EventService.HookMethod(
            PatchIdentifier,
            drawIcon,
            DrawIconPrefix,
            ILuaCsHook.HookMethodType.Before,
            owner: this);

        LuaCsSetup.Instance.Logger.LogMessage("[ElysianRealm] Client portrait patch registered.");
    }

    public void Initialize()
    {
    }

    public void OnLoadCompleted()
    {
    }

    public void Dispose()
    {
        portraitSprite?.Remove();
        portraitSprite = null;
        portraitFullPath = null;
        portraitLoadFailed = false;
        ownerPackage = null;
    }

    private static object DrawIconPrefix(object self, Dictionary<string, object> args)
    {
        if (self is not CharacterInfo info || info.Job?.Prefab?.Identifier != TargetJobIdentifier)
        {
            return null;
        }

        if (!TryLoadPortrait())
        {
            return null;
        }

        SpriteBatch spriteBatch = args["spriteBatch"] as SpriteBatch;
        if (spriteBatch == null ||
            args["screenPos"] is not Vector2 screenPos ||
            args["targetAreaSize"] is not Vector2 targetAreaSize)
        {
            return null;
        }

        bool flip = args.TryGetValue("flip", out object flipValue) && flipValue is bool flipBool && flipBool;
        SpriteEffects spriteEffects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        Vector2 origin = portraitSprite.Origin;
        if (flip)
        {
            origin.X = portraitSprite.SourceRect.Width - origin.X;
        }

        float scale = Math.Min(
            targetAreaSize.X / Math.Max(1.0f, portraitSprite.size.X),
            targetAreaSize.Y / Math.Max(1.0f, portraitSprite.size.Y));

        portraitSprite.Draw(spriteBatch, screenPos, Color.White, origin, scale: scale, spriteEffect: spriteEffects);

        return true;
    }

    private static bool TryLoadPortrait()
    {
        if (portraitLoadFailed)
        {
            return false;
        }

        if (portraitSprite?.Texture != null)
        {
            return true;
        }

        if (ownerPackage == null &&
            !LuaCsSetup.Instance.PluginManagementService.TryGetPackageForPlugin<ElysianPortraitPlugin>(out ownerPackage))
        {
            LuaCsSetup.Instance.Logger.LogWarning("[ElysianRealm] Cannot resolve owner package; portrait patch will fall back to vanilla.");
            portraitLoadFailed = true;
            return false;
        }

        portraitFullPath ??= System.IO.Path.Combine(
            ownerPackage.Dir,
            PortraitRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

        try
        {
            portraitSprite = new Sprite(portraitFullPath, sourceRectangle: null, origin: new Vector2(0.5f, 0.5f));
            if (portraitSprite.Texture == null)
            {
                LuaCsSetup.Instance.Logger.LogWarning($"[ElysianRealm] Portrait texture failed to load: {portraitFullPath}");
                portraitSprite.Remove();
                portraitSprite = null;
                portraitLoadFailed = true;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            LuaCsSetup.Instance.Logger.HandleException(ex, "[ElysianRealm] Failed to load portrait: ");
            portraitSprite?.Remove();
            portraitSprite = null;
            portraitLoadFailed = true;
            return false;
        }
    }
}
