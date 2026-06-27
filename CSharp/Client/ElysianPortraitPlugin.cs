using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Barotrauma;
using Barotrauma.LuaCs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.ElysianRealm
{
    public sealed class ElysianPortraitPlugin : IAssemblyPlugin
    {
        private const string PatchIdentifier = "elysianrealm.clientportrait.characterinfo.drawicon";
        private const string TargetJobIdentifier = "realme";
        private const string PortraitRelativePath = "Assets/UI/elysia_portrait.png";

        private static ContentPackage ownerPackage;
        private static Sprite portraitSprite;
        private static string portraitFullPath;
        private static bool portraitLoadFailed;
        private static bool loggedFirstTarget;
        private static bool loggedFirstOverlay;
        private static bool loggedArgumentFailure;

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
                LuaCsLogger.LogError("[ElysianRealm] CharacterInfo.DrawIcon was not found; portrait patch disabled.");
                return;
            }

            LuaCsSetup.Instance.EventService.HookMethod(
                PatchIdentifier,
                drawIcon,
                DrawIconPostfix,
                ILuaCsHook.HookMethodType.After,
                owner: this);

            string packageDir = ownerPackage == null ? "<unresolved>" : ownerPackage.Dir;
            LuaCsLogger.LogMessage("[ElysianRealm] Client portrait patch registered. Package=" + packageDir);
            EnsureCompanionPlugins();
        }

        public void Initialize()
        {
        }

        public void OnLoadCompleted()
        {
        }

        public void Dispose()
        {
            ElysianGameplayPlugin.Shutdown();
            ElysianBuffPlugin.Shutdown();

            if (portraitSprite != null)
            {
                portraitSprite.Remove();
            }
            portraitSprite = null;
            portraitFullPath = null;
            portraitLoadFailed = false;
            ownerPackage = null;
            loggedFirstTarget = false;
            loggedFirstOverlay = false;
            loggedArgumentFailure = false;
        }

        private void EnsureCompanionPlugins()
        {
            try
            {
                ElysianBuffPlugin.EnsureInitialized(this, ownerPackage);
                ElysianGameplayPlugin.EnsureInitialized(this, ownerPackage);
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError("[ElysianRealm] Failed to start companion plugins from portrait entry: " + ex.GetType().Name);
                LuaCsLogger.HandleException(ex, LuaCsMessageOrigin.LuaMod);
            }
        }

        private static object DrawIconPostfix(object self, Dictionary<string, object> args)
        {
            CharacterInfo info = self as CharacterInfo;
            if (info == null || info.Job == null || info.Job.Prefab == null)
            {
                return null;
            }

            string jobIdentifier = info.Job.Prefab.Identifier.ToString();
            if (!string.Equals(jobIdentifier, TargetJobIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!loggedFirstTarget)
            {
                loggedFirstTarget = true;
                LuaCsLogger.LogMessage("[ElysianRealm] Realme portrait target matched: " + info.Name);
            }

            if (!TryLoadPortrait())
            {
                return null;
            }

            object spriteBatchValue;
            object screenPosValue;
            object targetAreaSizeValue;
            if (!args.TryGetValue("spriteBatch", out spriteBatchValue) ||
                !args.TryGetValue("screenPos", out screenPosValue) ||
                !args.TryGetValue("targetAreaSize", out targetAreaSizeValue))
            {
                LogArgumentFailureOnce();
                return null;
            }

            SpriteBatch spriteBatch = spriteBatchValue as SpriteBatch;
            if (spriteBatch == null || !(screenPosValue is Vector2) || !(targetAreaSizeValue is Vector2))
            {
                LogArgumentFailureOnce();
                return null;
            }

            Vector2 screenPos = (Vector2)screenPosValue;
            Vector2 targetAreaSize = (Vector2)targetAreaSizeValue;
            object flipValue;
            bool flip = args.TryGetValue("flip", out flipValue) && flipValue is bool && (bool)flipValue;
            SpriteEffects spriteEffects = flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            Rectangle sourceRect = portraitSprite.SourceRect;
            float scale = Math.Min(
                targetAreaSize.X / Math.Max(1.0f, sourceRect.Width),
                targetAreaSize.Y / Math.Max(1.0f, sourceRect.Height));

            int width = Math.Max(1, (int)Math.Round(sourceRect.Width * scale));
            int height = Math.Max(1, (int)Math.Round(sourceRect.Height * scale));
            Rectangle targetRect = new Rectangle(
                (int)Math.Round(screenPos.X - width / 2.0f),
                (int)Math.Round(screenPos.Y - height / 2.0f),
                width,
                height);

            spriteBatch.Draw(
                portraitSprite.Texture,
                targetRect,
                sourceRect,
                Color.White,
                0.0f,
                Vector2.Zero,
                spriteEffects,
                0.001f);

            if (!loggedFirstOverlay)
            {
                loggedFirstOverlay = true;
                LuaCsLogger.LogMessage("[ElysianRealm] Realme portrait overlay drawn.");
            }

            return null;
        }

        private static void LogArgumentFailureOnce()
        {
            if (loggedArgumentFailure)
            {
                return;
            }

            loggedArgumentFailure = true;
            LuaCsLogger.LogError("[ElysianRealm] CharacterInfo.DrawIcon arguments did not match the expected LuaCs hook table.");
        }

        private static bool TryLoadPortrait()
        {
            if (portraitLoadFailed)
            {
                return false;
            }

            if (portraitSprite != null && portraitSprite.Texture != null)
            {
                return true;
            }

            if (ownerPackage == null &&
                !LuaCsSetup.Instance.PluginManagementService.TryGetPackageForPlugin<ElysianPortraitPlugin>(out ownerPackage))
            {
                LuaCsLogger.LogError("[ElysianRealm] Cannot resolve owner package; portrait patch will fall back to vanilla.");
                portraitLoadFailed = true;
                return false;
            }

            portraitFullPath = Path.Combine(
                ownerPackage.Dir,
                PortraitRelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(portraitFullPath))
            {
                LuaCsLogger.LogError("[ElysianRealm] Portrait file not found: " + portraitFullPath);
                portraitLoadFailed = true;
                return false;
            }

            try
            {
                portraitSprite = new Sprite(portraitFullPath, sourceRectangle: null, origin: new Vector2(0.5f, 0.5f));
                if (portraitSprite.Texture == null)
                {
                    LuaCsLogger.LogError("[ElysianRealm] Portrait texture failed to load: " + portraitFullPath);
                    portraitSprite.Remove();
                    portraitSprite = null;
                    portraitLoadFailed = true;
                    return false;
                }

                LuaCsLogger.LogMessage(
                    "[ElysianRealm] Portrait loaded: " + portraitFullPath + " (" +
                    portraitSprite.SourceRect.Width + "x" + portraitSprite.SourceRect.Height + ")");
                return true;
            }
            catch (Exception ex)
            {
                LuaCsLogger.HandleException(ex, LuaCsMessageOrigin.LuaMod);
                if (portraitSprite != null)
                {
                    portraitSprite.Remove();
                }
                portraitSprite = null;
                portraitLoadFailed = true;
                return false;
            }
        }
    }
}
