using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI.Events;
using StardewModdingAPI.Framework.Reflection;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Tools;
using xTile.Dimensions;
using xTile.Layers;
using SFarmer = StardewValley.Farmer;

namespace StardewModdingAPI.Framework
{
    /// <summary>SMAPI's extension of the game's core <see cref="Game1"/>, used to inject events.</summary>
    internal class SGame : Game1
    {
        /*********
        ** Properties
        *********/
        /****
        ** SMAPI state
        ****/
        /// <summary>The number of ticks until SMAPI should notify mods that the game has loaded.</summary>
        /// <remarks>Skipping a few frames ensures the game finishes initialising the world before mods try to change it.</remarks>
        private int AfterLoadTimer = 5;

        /// <summary>Whether the player has loaded a save and the world has finished initialising.</summary>
        private bool IsWorldReady => this.AfterLoadTimer < 0;

        /// <summary>Whether the game is returning to the menu.</summary>
        private bool IsExiting;

        /// <summary>Whether the game's zoom level is at 100% (i.e. nothing should be scaled).</summary>
        public bool ZoomLevelIsOne => Game1.options.zoomLevel.Equals(1.0f);

        /// <summary>Encapsulates monitoring and logging.</summary>
        private readonly IMonitor Monitor;

        /****
        ** Game state
        ****/
        /// <summary>Arrays of pressed controller buttons indexed by <see cref="PlayerIndex"/>.</summary>
        private Buttons[][] PreviouslyPressedButtons;

        /// <summary>A record of the keyboard state (i.e. the up/down state for each button) as of the latest tick.</summary>
        private KeyboardState KStateNow;

        /// <summary>A record of the keyboard state (i.e. the up/down state for each button) as of the previous tick.</summary>
        private KeyboardState KStatePrior;

        /// <summary>A record of the mouse state (i.e. the cursor position, scroll amount, and the up/down state for each button) as of the latest tick.</summary>
        private MouseState MStateNow;

        /// <summary>A record of the mouse state (i.e. the cursor position, scroll amount, and the up/down state for each button) as of the previous tick.</summary>
        private MouseState MStatePrior;

        /// <summary>The current mouse position on the screen adjusted for the zoom level.</summary>
        private Point MPositionNow;

        /// <summary>The previous mouse position on the screen adjusted for the zoom level.</summary>
        private Point MPositionPrior;

        /// <summary>The keys that were pressed as of the latest tick.</summary>
        private Keys[] CurrentlyPressedKeys => this.KStateNow.GetPressedKeys();

        /// <summary>The keys that were pressed as of the previous tick.</summary>
        private Keys[] PreviouslyPressedKeys => this.KStatePrior.GetPressedKeys();

        /// <summary>The keys that just entered the down state.</summary>
        private Keys[] FramePressedKeys => this.CurrentlyPressedKeys.Except(this.PreviouslyPressedKeys).ToArray();

        /// <summary>The keys that just entered the up state.</summary>
        private Keys[] FrameReleasedKeys => this.PreviouslyPressedKeys.Except(this.CurrentlyPressedKeys).ToArray();

        /// <summary>A hash of <see cref="Game1.locations"/> at last check.</summary>
        private int PreviousGameLocations;

        /// <summary>A hash of the current location's <see cref="GameLocation.objects"/> at last check.</summary>
        private int PreviousLocationObjects;

        /// <summary>The player's inventory at last check.</summary>
        private IDictionary<Item, int> PreviousItems;

        /// <summary>The player's combat skill level at last check.</summary>
        private int PreviousCombatLevel;

        /// <summary>The player's farming skill level at last check.</summary>
        private int PreviousFarmingLevel;

        /// <summary>The player's fishing skill level at last check.</summary>
        private int PreviousFishingLevel;

        /// <summary>The player's foraging skill level at last check.</summary>
        private int PreviousForagingLevel;

        /// <summary>The player's mining skill level at last check.</summary>
        private int PreviousMiningLevel;

        /// <summary>The player's luck skill level at last check.</summary>
        private int PreviousLuckLevel;

        /// <summary>The player's location at last check.</summary>
        private GameLocation PreviousGameLocation;

        /// <summary>The active game menu at last check.</summary>
        private IClickableMenu PreviousActiveMenu;

        /// <summary>The mine level at last check.</summary>
        private int PreviousMineLevel;

        /// <summary>The time of day (in 24-hour military format) at last check.</summary>
        private int PreviousTime;

        /// <summary>The day of month (1–28) at last check.</summary>
        private int PreviousDay;

        /// <summary>The season name (winter, spring, summer, or fall) at last check.</summary>
        private string PreviousSeason;

        /// <summary>The year number at last check.</summary>
        private int PreviousYear;

        /// <summary>Whether the game was transitioning to a new day at last check.</summary>
        private bool PreviousIsNewDay;

        /// <summary>The player character at last check.</summary>
        private SFarmer PreviousFarmer;

        /// <summary>The previous content locale.</summary>
        private LocalizedContentManager.LanguageCode? PreviousLocale;

        /// <summary>An index incremented on every tick and reset every 60th tick (0–59).</summary>
        private int CurrentUpdateTick;

        /// <summary>Whether this is the very first update tick since the game started.</summary>
        private bool FirstUpdate;

        /// <summary>The current game instance.</summary>
        private static SGame Instance;

        /****
        ** Private wrappers
        ****/
        // ReSharper disable ArrangeStaticMemberQualifier, ArrangeThisQualifier, InconsistentNaming
        /// <summary>Used to access private fields and methods.</summary>
        private static readonly IReflectionHelper Reflection = new ReflectionHelper();
        private static List<float> _fpsList => SGame.Reflection.GetPrivateField<List<float>>(typeof(Game1), nameof(_fpsList)).GetValue();
        private static Stopwatch _fpsStopwatch => SGame.Reflection.GetPrivateField<Stopwatch>(typeof(Game1), nameof(SGame._fpsStopwatch)).GetValue();
        private static float _fps
        {
            set { SGame.Reflection.GetPrivateField<float>(typeof(Game1), nameof(_fps)).SetValue(value); }
        }
        private static Task _newDayTask => SGame.Reflection.GetPrivateField<Task>(typeof(Game1), nameof(_newDayTask)).GetValue();
        private Color bgColor => SGame.Reflection.GetPrivateField<Color>(this, nameof(bgColor)).GetValue();
        public RenderTarget2D screenWrapper => SGame.Reflection.GetPrivateProperty<RenderTarget2D>(this, "screen").GetValue(); // deliberately renamed to avoid an infinite loop
        public BlendState lightingBlend => SGame.Reflection.GetPrivateField<BlendState>(this, nameof(lightingBlend)).GetValue();
        private readonly Action drawFarmBuildings = () => SGame.Reflection.GetPrivateMethod(SGame.Instance, nameof(drawFarmBuildings)).Invoke(new object[0]);
        private readonly Action drawHUD = () => SGame.Reflection.GetPrivateMethod(SGame.Instance, nameof(drawHUD)).Invoke(new object[0]);
        private readonly Action drawDialogueBox = () => SGame.Reflection.GetPrivateMethod(SGame.Instance, nameof(drawDialogueBox)).Invoke(new object[0]);
        private readonly Action renderScreenBuffer = () => SGame.Reflection.GetPrivateMethod(SGame.Instance, nameof(renderScreenBuffer)).Invoke(new object[0]);
        // ReSharper restore ArrangeStaticMemberQualifier, ArrangeThisQualifier, InconsistentNaming


        /*********
        ** Protected methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="monitor">Encapsulates monitoring and logging.</param>
        internal SGame(IMonitor monitor)
        {
            this.Monitor = monitor;
            this.FirstUpdate = true;
            SGame.Instance = this;
        }

        /****
        ** Intercepted methods & events
        ****/
        /// <summary>The method called during game launch after configuring XNA or MonoGame. The game window hasn't been opened by this point.</summary>
        protected override void Initialize()
        {
            this.PreviouslyPressedButtons = new Buttons[4][];
            for (var i = 0; i < 4; ++i)
                this.PreviouslyPressedButtons[i] = new Buttons[0];

            base.Initialize();
            GameEvents.InvokeInitialize(this.Monitor);
        }

        /// <summary>Constructor a content manager to read XNB files.</summary>
        /// <param name="serviceProvider">The service provider to use to locate services.</param>
        /// <param name="rootDirectory">The root directory to search for content.</param>
        protected override LocalizedContentManager CreateContentManager(IServiceProvider serviceProvider, string rootDirectory)
        {
            return new SContentManager(this.Content.ServiceProvider, this.Content.RootDirectory, this.Monitor);
        }

        /// <summary>The method called before XNA or MonoGame loads or reloads graphics resources.</summary>
        protected override void LoadContent()
        {
            base.LoadContent();
            GameEvents.InvokeLoadContent(this.Monitor);
        }

        /// <summary>The method called when the game is updating its state. This happens roughly 60 times per second.</summary>
        /// <param name="gameTime">A snapshot of the game timing state.</param>
        protected override void Update(GameTime gameTime)
        {
            // While a background new-day task is in progress, the game skips its own update logic
            // and defers to the XNA Update method. Running mod code in parallel to the background
            // update is risky, because data changes can conflict (e.g. collection changed during
            // enumeration errors) and data may change unexpectedly from one mod instruction to the
            // next.
            // 
            // Therefore we can just run Game1.Update here without raising any SMAPI events. There's
            // a small chance that the task will finish after we defer but before the game checks,
            // which means technically events should be raised, but the effects of missing one
            // update tick are neglible and not worth the complications of bypassing Game1.Update.
            if (SGame._newDayTask != null)
            {
                base.Update(gameTime);
                return;
            }

            // raise game loaded
            if (this.FirstUpdate)
                GameEvents.InvokeGameLoaded(this.Monitor);

            // update SMAPI events
            this.UpdateEventCalls();

            // let game update
            try
            {
                base.Update(gameTime);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"An error occured in the base update loop: {ex.GetLogSummary()}", LogLevel.Error);
                Console.ReadKey();
            }

            // raise update events
            GameEvents.InvokeUpdateTick(this.Monitor);
            if (this.FirstUpdate)
            {
                GameEvents.InvokeFirstUpdateTick(this.Monitor);
                this.FirstUpdate = false;
            }
            if (this.CurrentUpdateTick % 2 == 0)
                GameEvents.InvokeSecondUpdateTick(this.Monitor);
            if (this.CurrentUpdateTick % 4 == 0)
                GameEvents.InvokeFourthUpdateTick(this.Monitor);
            if (this.CurrentUpdateTick % 8 == 0)
                GameEvents.InvokeEighthUpdateTick(this.Monitor);
            if (this.CurrentUpdateTick % 15 == 0)
                GameEvents.InvokeQuarterSecondTick(this.Monitor);
            if (this.CurrentUpdateTick % 30 == 0)
                GameEvents.InvokeHalfSecondTick(this.Monitor);
            if (this.CurrentUpdateTick % 60 == 0)
                GameEvents.InvokeOneSecondTick(this.Monitor);
            this.CurrentUpdateTick += 1;
            if (this.CurrentUpdateTick >= 60)
                this.CurrentUpdateTick = 0;

            // track keyboard state
            this.KStatePrior = this.KStateNow;

            // track controller button state
            for (var i = PlayerIndex.One; i <= PlayerIndex.Four; i++)
                this.PreviouslyPressedButtons[(int)i] = this.GetButtonsDown(i);
        }

        /// <summary>The method called to draw everything to the screen.</summary>
        /// <param name="gameTime">A snapshot of the game timing state.</param>
        /// <remarks>This implementation is identical to <see cref="Game1.Draw"/>, except for try..catch around menu draw code, private field references replaced by wrappers, and added events.</remarks>
        [SuppressMessage("ReSharper", "CompareOfFloatsByEqualityOperator", Justification = "copied from game code as-is")]
        [SuppressMessage("ReSharper", "LocalVariableHidesMember", Justification = "copied from game code as-is")]
        [SuppressMessage("ReSharper", "PossibleLossOfFraction", Justification = "copied from game code as-is")]
        [SuppressMessage("ReSharper", "RedundantArgumentDefaultValue", Justification = "copied from game code as-is")]
        [SuppressMessage("ReSharper", "RedundantCast", Justification = "copied from game code as-is")]
        [SuppressMessage("ReSharper", "RedundantExplicitNullableCreation", Justification = "copied from game code as-is")]
        [SuppressMessage("ReSharper", "RedundantTypeArgumentsOfMethod", Justification = "copied from game code as-is")]
        protected override void Draw(GameTime gameTime)
        {
            try
            {
                if (Game1.debugMode)
                {
                    if (SGame._fpsStopwatch.IsRunning)
                    {
                        float totalSeconds = (float)SGame._fpsStopwatch.Elapsed.TotalSeconds;
                        SGame._fpsList.Add(totalSeconds);
                        while (SGame._fpsList.Count >= 120)
                            SGame._fpsList.RemoveAt(0);
                        float num = 0.0f;
                        foreach (float fps in SGame._fpsList)
                            num += fps;
                        SGame._fps = (float)(1.0 / ((double)num / (double)SGame._fpsList.Count));
                    }
                    SGame._fpsStopwatch.Restart();
                }
                else
                {
                    if (SGame._fpsStopwatch.IsRunning)
                        SGame._fpsStopwatch.Reset();
                    SGame._fps = 0.0f;
                    SGame._fpsList.Clear();
                }
                if (SGame._newDayTask != null)
                {
                    this.GraphicsDevice.Clear(this.bgColor);
                    //base.Draw(gameTime);
                }
                else
                {
                    if ((double)Game1.options.zoomLevel != 1.0)
                        this.GraphicsDevice.SetRenderTarget(this.screenWrapper);
                    if (this.IsSaving)
                    {
                        this.GraphicsDevice.Clear(this.bgColor);
                        IClickableMenu activeClickableMenu = Game1.activeClickableMenu;
                        if (activeClickableMenu != null)
                        {
                            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                            try
                            {
                                GraphicsEvents.InvokeOnPreRenderGuiEvent(this.Monitor);
                                activeClickableMenu.draw(Game1.spriteBatch);
                                GraphicsEvents.InvokeOnPostRenderGuiEvent(this.Monitor);
                            }
                            catch (Exception ex)
                            {
                                this.Monitor.Log($"The {activeClickableMenu.GetType().FullName} menu crashed while drawing itself during save. SMAPI will force it to exit to avoid crashing the game.\n{ex.GetLogSummary()}", LogLevel.Error);
                                activeClickableMenu.exitThisMenu();
                            }
                            Game1.spriteBatch.End();
                        }
                        //base.Draw(gameTime);
                        this.renderScreenBuffer();
                    }
                    else
                    {
                        this.GraphicsDevice.Clear(this.bgColor);
                        if (Game1.activeClickableMenu != null && Game1.options.showMenuBackground && Game1.activeClickableMenu.showWithoutTransparencyIfOptionIsSet())
                        {
                            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                            try
                            {
                                Game1.activeClickableMenu.drawBackground(Game1.spriteBatch);
                                GraphicsEvents.InvokeOnPreRenderGuiEvent(this.Monitor);
                                Game1.activeClickableMenu.draw(Game1.spriteBatch);
                                GraphicsEvents.InvokeOnPostRenderGuiEvent(this.Monitor);
                            }
                            catch (Exception ex)
                            {
                                this.Monitor.Log($"The {Game1.activeClickableMenu.GetType().FullName} menu crashed while drawing itself. SMAPI will force it to exit to avoid crashing the game.\n{ex.GetLogSummary()}", LogLevel.Error);
                                Game1.activeClickableMenu.exitThisMenu();
                            }
                            Game1.spriteBatch.End();
                            if ((double)Game1.options.zoomLevel != 1.0)
                            {
                                this.GraphicsDevice.SetRenderTarget((RenderTarget2D)null);
                                this.GraphicsDevice.Clear(this.bgColor);
                                Game1.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone);
                                Game1.spriteBatch.Draw((Texture2D)this.screenWrapper, Vector2.Zero, new Microsoft.Xna.Framework.Rectangle?(this.screenWrapper.Bounds), Color.White, 0.0f, Vector2.Zero, Game1.options.zoomLevel, SpriteEffects.None, 1f);
                                Game1.spriteBatch.End();
                            }
                            if (Game1.overlayMenu == null)
                                return;
                            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                            Game1.overlayMenu.draw(Game1.spriteBatch);
                            Game1.spriteBatch.End();
                        }
                        else if ((int)Game1.gameMode == 11)
                        {
                            Game1.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                            Game1.spriteBatch.DrawString(Game1.dialogueFont, Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3685"), new Vector2(16f, 16f), Color.HotPink);
                            Game1.spriteBatch.DrawString(Game1.dialogueFont, Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3686"), new Vector2(16f, 32f), new Color(0, (int)byte.MaxValue, 0));
                            Game1.spriteBatch.DrawString(Game1.dialogueFont, Game1.parseText(Game1.errorMessage, Game1.dialogueFont, Game1.graphics.GraphicsDevice.Viewport.Width), new Vector2(16f, 48f), Color.White);
                            Game1.spriteBatch.End();
                        }
                        else if (Game1.currentMinigame != null)
                        {
                            Game1.currentMinigame.draw(Game1.spriteBatch);
                            if (Game1.globalFade && !Game1.menuUp && (!Game1.nameSelectUp || Game1.messagePause))
                            {
                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                                Game1.spriteBatch.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * ((int)Game1.gameMode == 0 ? 1f - Game1.fadeToBlackAlpha : Game1.fadeToBlackAlpha));
                                Game1.spriteBatch.End();
                            }
                            if ((double)Game1.options.zoomLevel != 1.0)
                            {
                                this.GraphicsDevice.SetRenderTarget((RenderTarget2D)null);
                                this.GraphicsDevice.Clear(this.bgColor);
                                Game1.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone);
                                Game1.spriteBatch.Draw((Texture2D)this.screenWrapper, Vector2.Zero, new Microsoft.Xna.Framework.Rectangle?(this.screenWrapper.Bounds), Color.White, 0.0f, Vector2.Zero, Game1.options.zoomLevel, SpriteEffects.None, 1f);
                                Game1.spriteBatch.End();
                            }
                            if (Game1.overlayMenu == null)
                                return;
                            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                            Game1.overlayMenu.draw(Game1.spriteBatch);
                            Game1.spriteBatch.End();
                        }
                        else if (Game1.showingEndOfNightStuff)
                        {
                            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                            if (Game1.activeClickableMenu != null)
                            {
                                try
                                {
                                    GraphicsEvents.InvokeOnPreRenderGuiEvent(this.Monitor);
                                    Game1.activeClickableMenu.draw(Game1.spriteBatch);
                                    GraphicsEvents.InvokeOnPostRenderGuiEvent(this.Monitor);
                                }
                                catch (Exception ex)
                                {
                                    this.Monitor.Log($"The {Game1.activeClickableMenu.GetType().FullName} menu crashed while drawing itself during end-of-night-stuff. SMAPI will force it to exit to avoid crashing the game.\n{ex.GetLogSummary()}", LogLevel.Error);
                                    Game1.activeClickableMenu.exitThisMenu();
                                }
                            }
                            Game1.spriteBatch.End();
                            if ((double)Game1.options.zoomLevel != 1.0)
                            {
                                this.GraphicsDevice.SetRenderTarget((RenderTarget2D)null);
                                this.GraphicsDevice.Clear(this.bgColor);
                                Game1.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone);
                                Game1.spriteBatch.Draw((Texture2D)this.screenWrapper, Vector2.Zero, new Microsoft.Xna.Framework.Rectangle?(this.screenWrapper.Bounds), Color.White, 0.0f, Vector2.Zero, Game1.options.zoomLevel, SpriteEffects.None, 1f);
                                Game1.spriteBatch.End();
                            }
                            if (Game1.overlayMenu == null)
                                return;
                            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                            Game1.overlayMenu.draw(Game1.spriteBatch);
                            Game1.spriteBatch.End();
                        }
                        else if ((int)Game1.gameMode == 6)
                        {
                            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                            string str1 = "";
                            for (int index = 0; (double)index < gameTime.TotalGameTime.TotalMilliseconds % 999.0 / 333.0; ++index)
                                str1 += ".";
                            string str2 = Game1.content.LoadString("Strings\\StringsFromCSFiles:Game1.cs.3688");
                            string str3 = str1;
                            string s = str2 + str3;
                            string str4 = "...";
                            string str5 = str2 + str4;
                            int widthOfString = SpriteText.getWidthOfString(str5);
                            int height = 64;
                            int x = 64;
                            int y = Game1.graphics.GraphicsDevice.Viewport.TitleSafeArea.Bottom - height;
                            SpriteText.drawString(Game1.spriteBatch, s, x, y, 999999, widthOfString, height, 1f, 0.88f, false, 0, str5, -1);
                            Game1.spriteBatch.End();
                            if ((double)Game1.options.zoomLevel != 1.0)
                            {
                                this.GraphicsDevice.SetRenderTarget((RenderTarget2D)null);
                                this.GraphicsDevice.Clear(this.bgColor);
                                Game1.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullNone);
                                Game1.spriteBatch.Draw((Texture2D)this.screenWrapper, Vector2.Zero, new Microsoft.Xna.Framework.Rectangle?(this.screenWrapper.Bounds), Color.White, 0.0f, Vector2.Zero, Game1.options.zoomLevel, SpriteEffects.None, 1f);
                                Game1.spriteBatch.End();
                            }
                            if (Game1.overlayMenu == null)
                                return;
                            Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                            Game1.overlayMenu.draw(Game1.spriteBatch);
                            Game1.spriteBatch.End();
                        }
                        else
                        {
                            Microsoft.Xna.Framework.Rectangle rectangle;
                            if ((int)Game1.gameMode == 0)
                            {
                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                            }
                            else
                            {
                                if (Game1.drawLighting)
                                {
                                    this.GraphicsDevice.SetRenderTarget(Game1.lightmap);
                                    this.GraphicsDevice.Clear(Color.White * 0.0f);
                                    Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                                    Game1.spriteBatch.Draw(Game1.staminaRect, Game1.lightmap.Bounds, Game1.currentLocation.name.Equals("UndergroundMine") ? Game1.mine.getLightingColor(gameTime) : (Game1.ambientLight.Equals(Color.White) || Game1.isRaining && Game1.currentLocation.isOutdoors ? Game1.outdoorLight : Game1.ambientLight));
                                    for (int index = 0; index < Game1.currentLightSources.Count; ++index)
                                    {
                                        if (Utility.isOnScreen(Game1.currentLightSources.ElementAt<LightSource>(index).position, (int)((double)Game1.currentLightSources.ElementAt<LightSource>(index).radius * (double)Game1.tileSize * 4.0)))
                                            Game1.spriteBatch.Draw(Game1.currentLightSources.ElementAt<LightSource>(index).lightTexture, Game1.GlobalToLocal(Game1.viewport, Game1.currentLightSources.ElementAt<LightSource>(index).position) / (float)(Game1.options.lightingQuality / 2), new Microsoft.Xna.Framework.Rectangle?(Game1.currentLightSources.ElementAt<LightSource>(index).lightTexture.Bounds), Game1.currentLightSources.ElementAt<LightSource>(index).color, 0.0f, new Vector2((float)Game1.currentLightSources.ElementAt<LightSource>(index).lightTexture.Bounds.Center.X, (float)Game1.currentLightSources.ElementAt<LightSource>(index).lightTexture.Bounds.Center.Y), Game1.currentLightSources.ElementAt<LightSource>(index).radius / (float)(Game1.options.lightingQuality / 2), SpriteEffects.None, 0.9f);
                                    }
                                    Game1.spriteBatch.End();
                                    this.GraphicsDevice.SetRenderTarget((double)Game1.options.zoomLevel == 1.0 ? (RenderTarget2D)null : this.screenWrapper);
                                }
                                if (Game1.bloomDay && Game1.bloom != null)
                                    Game1.bloom.BeginDraw();
                                this.GraphicsDevice.Clear(this.bgColor);
                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                                GraphicsEvents.InvokeOnPreRenderEvent(this.Monitor);
                                if (Game1.background != null)
                                    Game1.background.draw(Game1.spriteBatch);
                                Game1.mapDisplayDevice.BeginScene(Game1.spriteBatch);
                                Game1.currentLocation.Map.GetLayer("Back").Draw(Game1.mapDisplayDevice, Game1.viewport, Location.Origin, false, Game1.pixelZoom);
                                Game1.currentLocation.drawWater(Game1.spriteBatch);
                                if (Game1.CurrentEvent == null)
                                {
                                    foreach (NPC character in Game1.currentLocation.characters)
                                    {
                                        if (!character.swimming && !character.hideShadow && (!character.isInvisible && !Game1.currentLocation.shouldShadowBeDrawnAboveBuildingsLayer(character.getTileLocation())))
                                            Game1.spriteBatch.Draw(Game1.shadowTexture, Game1.GlobalToLocal(Game1.viewport, character.position + new Vector2((float)(character.sprite.spriteWidth * Game1.pixelZoom) / 2f, (float)(character.GetBoundingBox().Height + (character.IsMonster ? 0 : Game1.pixelZoom * 3)))), new Microsoft.Xna.Framework.Rectangle?(Game1.shadowTexture.Bounds), Color.White, 0.0f, new Vector2((float)Game1.shadowTexture.Bounds.Center.X, (float)Game1.shadowTexture.Bounds.Center.Y), ((float)Game1.pixelZoom + (float)character.yJumpOffset / 40f) * character.scale, SpriteEffects.None, Math.Max(0.0f, (float)character.getStandingY() / 10000f) - 1E-06f);
                                    }
                                }
                                else
                                {
                                    foreach (NPC actor in Game1.CurrentEvent.actors)
                                    {
                                        if (!actor.swimming && !actor.hideShadow && !Game1.currentLocation.shouldShadowBeDrawnAboveBuildingsLayer(actor.getTileLocation()))
                                            Game1.spriteBatch.Draw(Game1.shadowTexture, Game1.GlobalToLocal(Game1.viewport, actor.position + new Vector2((float)(actor.sprite.spriteWidth * Game1.pixelZoom) / 2f, (float)(actor.GetBoundingBox().Height + (actor.IsMonster ? 0 : (actor.sprite.spriteHeight <= 16 ? -Game1.pixelZoom : Game1.pixelZoom * 3))))), new Microsoft.Xna.Framework.Rectangle?(Game1.shadowTexture.Bounds), Color.White, 0.0f, new Vector2((float)Game1.shadowTexture.Bounds.Center.X, (float)Game1.shadowTexture.Bounds.Center.Y), ((float)Game1.pixelZoom + (float)actor.yJumpOffset / 40f) * actor.scale, SpriteEffects.None, Math.Max(0.0f, (float)actor.getStandingY() / 10000f) - 1E-06f);
                                    }
                                }
                                Microsoft.Xna.Framework.Rectangle bounds;
                                if (Game1.displayFarmer && !Game1.player.swimming && (!Game1.player.isRidingHorse() && !Game1.currentLocation.shouldShadowBeDrawnAboveBuildingsLayer(Game1.player.getTileLocation())))
                                {
                                    SpriteBatch spriteBatch = Game1.spriteBatch;
                                    Texture2D shadowTexture = Game1.shadowTexture;
                                    Vector2 local = Game1.GlobalToLocal(Game1.player.position + new Vector2(32f, 24f));
                                    Microsoft.Xna.Framework.Rectangle? sourceRectangle = new Microsoft.Xna.Framework.Rectangle?(Game1.shadowTexture.Bounds);
                                    Color white = Color.White;
                                    double num1 = 0.0;
                                    double x = (double)Game1.shadowTexture.Bounds.Center.X;
                                    bounds = Game1.shadowTexture.Bounds;
                                    double y = (double)bounds.Center.Y;
                                    Vector2 origin = new Vector2((float)x, (float)y);
                                    double num2 = 4.0 - (!Game1.player.running && !Game1.player.usingTool || Game1.player.FarmerSprite.indexInCurrentAnimation <= 1 ? 0.0 : (double)Math.Abs(FarmerRenderer.featureYOffsetPerFrame[Game1.player.FarmerSprite.CurrentFrame]) * 0.5);
                                    int num3 = 0;
                                    double num4 = 0.0;
                                    spriteBatch.Draw(shadowTexture, local, sourceRectangle, white, (float)num1, origin, (float)num2, (SpriteEffects)num3, (float)num4);
                                }
                                Game1.currentLocation.Map.GetLayer("Buildings").Draw(Game1.mapDisplayDevice, Game1.viewport, Location.Origin, false, Game1.pixelZoom);
                                Game1.mapDisplayDevice.EndScene();
                                Game1.spriteBatch.End();
                                Game1.spriteBatch.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                                if (Game1.CurrentEvent == null)
                                {
                                    foreach (NPC character in Game1.currentLocation.characters)
                                    {
                                        if (!character.swimming && !character.hideShadow && Game1.currentLocation.shouldShadowBeDrawnAboveBuildingsLayer(character.getTileLocation()))
                                        {
                                            SpriteBatch spriteBatch = Game1.spriteBatch;
                                            Texture2D shadowTexture = Game1.shadowTexture;
                                            Vector2 local = Game1.GlobalToLocal(Game1.viewport, character.position + new Vector2((float)(character.sprite.spriteWidth * Game1.pixelZoom) / 2f, (float)(character.GetBoundingBox().Height + (character.IsMonster ? 0 : Game1.pixelZoom * 3))));
                                            Microsoft.Xna.Framework.Rectangle? sourceRectangle = new Microsoft.Xna.Framework.Rectangle?(Game1.shadowTexture.Bounds);
                                            Color white = Color.White;
                                            double num1 = 0.0;
                                            bounds = Game1.shadowTexture.Bounds;
                                            double x = (double)bounds.Center.X;
                                            bounds = Game1.shadowTexture.Bounds;
                                            double y = (double)bounds.Center.Y;
                                            Vector2 origin = new Vector2((float)x, (float)y);
                                            double num2 = ((double)Game1.pixelZoom + (double)character.yJumpOffset / 40.0) * (double)character.scale;
                                            int num3 = 0;
                                            double num4 = (double)Math.Max(0.0f, (float)character.getStandingY() / 10000f) - 9.99999997475243E-07;
                                            spriteBatch.Draw(shadowTexture, local, sourceRectangle, white, (float)num1, origin, (float)num2, (SpriteEffects)num3, (float)num4);
                                        }
                                    }
                                }
                                else
                                {
                                    foreach (NPC actor in Game1.CurrentEvent.actors)
                                    {
                                        if (!actor.swimming && !actor.hideShadow && Game1.currentLocation.shouldShadowBeDrawnAboveBuildingsLayer(actor.getTileLocation()))
                                        {
                                            SpriteBatch spriteBatch = Game1.spriteBatch;
                                            Texture2D shadowTexture = Game1.shadowTexture;
                                            Vector2 local = Game1.GlobalToLocal(Game1.viewport, actor.position + new Vector2((float)(actor.sprite.spriteWidth * Game1.pixelZoom) / 2f, (float)(actor.GetBoundingBox().Height + (actor.IsMonster ? 0 : Game1.pixelZoom * 3))));
                                            Microsoft.Xna.Framework.Rectangle? sourceRectangle = new Microsoft.Xna.Framework.Rectangle?(Game1.shadowTexture.Bounds);
                                            Color white = Color.White;
                                            double num1 = 0.0;
                                            bounds = Game1.shadowTexture.Bounds;
                                            double x = (double)bounds.Center.X;
                                            bounds = Game1.shadowTexture.Bounds;
                                            double y = (double)bounds.Center.Y;
                                            Vector2 origin = new Vector2((float)x, (float)y);
                                            double num2 = ((double)Game1.pixelZoom + (double)actor.yJumpOffset / 40.0) * (double)actor.scale;
                                            int num3 = 0;
                                            double num4 = (double)Math.Max(0.0f, (float)actor.getStandingY() / 10000f) - 9.99999997475243E-07;
                                            spriteBatch.Draw(shadowTexture, local, sourceRectangle, white, (float)num1, origin, (float)num2, (SpriteEffects)num3, (float)num4);
                                        }
                                    }
                                }
                                if (Game1.displayFarmer && !Game1.player.swimming && (!Game1.player.isRidingHorse() && Game1.currentLocation.shouldShadowBeDrawnAboveBuildingsLayer(Game1.player.getTileLocation())))
                                {
                                    SpriteBatch spriteBatch = Game1.spriteBatch;
                                    Texture2D shadowTexture = Game1.shadowTexture;
                                    Vector2 local = Game1.GlobalToLocal(Game1.player.position + new Vector2(32f, 24f));
                                    Microsoft.Xna.Framework.Rectangle? sourceRectangle = new Microsoft.Xna.Framework.Rectangle?(Game1.shadowTexture.Bounds);
                                    Color white = Color.White;
                                    double num1 = 0.0;
                                    double x = (double)Game1.shadowTexture.Bounds.Center.X;
                                    rectangle = Game1.shadowTexture.Bounds;
                                    double y = (double)rectangle.Center.Y;
                                    Vector2 origin = new Vector2((float)x, (float)y);
                                    double num2 = 4.0 - (!Game1.player.running && !Game1.player.usingTool || Game1.player.FarmerSprite.indexInCurrentAnimation <= 1 ? 0.0 : (double)Math.Abs(FarmerRenderer.featureYOffsetPerFrame[Game1.player.FarmerSprite.CurrentFrame]) * 0.5);
                                    int num3 = 0;
                                    double num4 = (double)Math.Max(0.0001f, (float)((double)Game1.player.getStandingY() / 10000.0 + 0.000110000000859145)) - 9.99999974737875E-05;
                                    spriteBatch.Draw(shadowTexture, local, sourceRectangle, white, (float)num1, origin, (float)num2, (SpriteEffects)num3, (float)num4);
                                }
                                if (Game1.displayFarmer)
                                    Game1.player.draw(Game1.spriteBatch);
                                if ((Game1.eventUp || Game1.killScreen) && (!Game1.killScreen && Game1.currentLocation.currentEvent != null))
                                    Game1.currentLocation.currentEvent.draw(Game1.spriteBatch);
                                if (Game1.player.currentUpgrade != null && Game1.player.currentUpgrade.daysLeftTillUpgradeDone <= 3 && Game1.currentLocation.Name.Equals("Farm"))
                                    Game1.spriteBatch.Draw(Game1.player.currentUpgrade.workerTexture, Game1.GlobalToLocal(Game1.viewport, Game1.player.currentUpgrade.positionOfCarpenter), new Microsoft.Xna.Framework.Rectangle?(Game1.player.currentUpgrade.getSourceRectangle()), Color.White, 0.0f, Vector2.Zero, 1f, SpriteEffects.None, (float)(((double)Game1.player.currentUpgrade.positionOfCarpenter.Y + (double)(Game1.tileSize * 3 / 4)) / 10000.0));
                                Game1.currentLocation.draw(Game1.spriteBatch);
                                if (Game1.eventUp && Game1.currentLocation.currentEvent != null)
                                {
                                    string messageToScreen = Game1.currentLocation.currentEvent.messageToScreen;
                                }
                                if (Game1.player.ActiveObject == null && (Game1.player.UsingTool || Game1.pickingTool) && (Game1.player.CurrentTool != null && (!Game1.player.CurrentTool.Name.Equals("Seeds") || Game1.pickingTool)))
                                    Game1.drawTool(Game1.player);
                                if (Game1.currentLocation.Name.Equals("Farm"))
                                    this.drawFarmBuildings();
                                if (Game1.tvStation >= 0)
                                    Game1.spriteBatch.Draw(Game1.tvStationTexture, Game1.GlobalToLocal(Game1.viewport, new Vector2((float)(6 * Game1.tileSize + Game1.tileSize / 4), (float)(2 * Game1.tileSize + Game1.tileSize / 2))), new Microsoft.Xna.Framework.Rectangle?(new Microsoft.Xna.Framework.Rectangle(Game1.tvStation * 24, 0, 24, 15)), Color.White, 0.0f, Vector2.Zero, 4f, SpriteEffects.None, 1E-08f);
                                if (Game1.panMode)
                                {
                                    Game1.spriteBatch.Draw(Game1.fadeToBlackRect, new Microsoft.Xna.Framework.Rectangle((int)Math.Floor((double)(Game1.getOldMouseX() + Game1.viewport.X) / (double)Game1.tileSize) * Game1.tileSize - Game1.viewport.X, (int)Math.Floor((double)(Game1.getOldMouseY() + Game1.viewport.Y) / (double)Game1.tileSize) * Game1.tileSize - Game1.viewport.Y, Game1.tileSize, Game1.tileSize), Color.Lime * 0.75f);
                                    foreach (Warp warp in Game1.currentLocation.warps)
                                        Game1.spriteBatch.Draw(Game1.fadeToBlackRect, new Microsoft.Xna.Framework.Rectangle(warp.X * Game1.tileSize - Game1.viewport.X, warp.Y * Game1.tileSize - Game1.viewport.Y, Game1.tileSize, Game1.tileSize), Color.Red * 0.75f);
                                }
                                Game1.mapDisplayDevice.BeginScene(Game1.spriteBatch);
                                Game1.currentLocation.Map.GetLayer("Front").Draw(Game1.mapDisplayDevice, Game1.viewport, Location.Origin, false, Game1.pixelZoom);
                                Game1.mapDisplayDevice.EndScene();
                                Game1.currentLocation.drawAboveFrontLayer(Game1.spriteBatch);
                                Game1.spriteBatch.End();
                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                                if (Game1.currentLocation.Name.Equals("Farm") && Game1.stats.SeedsSown >= 200U)
                                {
                                    Game1.spriteBatch.Draw(Game1.debrisSpriteSheet, Game1.GlobalToLocal(Game1.viewport, new Vector2((float)(3 * Game1.tileSize + Game1.tileSize / 4), (float)(Game1.tileSize + Game1.tileSize / 3))), new Microsoft.Xna.Framework.Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.debrisSpriteSheet, 16, -1, -1)), Color.White);
                                    Game1.spriteBatch.Draw(Game1.debrisSpriteSheet, Game1.GlobalToLocal(Game1.viewport, new Vector2((float)(4 * Game1.tileSize + Game1.tileSize), (float)(2 * Game1.tileSize + Game1.tileSize))), new Microsoft.Xna.Framework.Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.debrisSpriteSheet, 16, -1, -1)), Color.White);
                                    Game1.spriteBatch.Draw(Game1.debrisSpriteSheet, Game1.GlobalToLocal(Game1.viewport, new Vector2((float)(5 * Game1.tileSize), (float)(2 * Game1.tileSize))), new Microsoft.Xna.Framework.Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.debrisSpriteSheet, 16, -1, -1)), Color.White);
                                    Game1.spriteBatch.Draw(Game1.debrisSpriteSheet, Game1.GlobalToLocal(Game1.viewport, new Vector2((float)(3 * Game1.tileSize + Game1.tileSize / 2), (float)(3 * Game1.tileSize))), new Microsoft.Xna.Framework.Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.debrisSpriteSheet, 16, -1, -1)), Color.White);
                                    Game1.spriteBatch.Draw(Game1.debrisSpriteSheet, Game1.GlobalToLocal(Game1.viewport, new Vector2((float)(5 * Game1.tileSize - Game1.tileSize / 4), (float)Game1.tileSize)), new Microsoft.Xna.Framework.Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.debrisSpriteSheet, 16, -1, -1)), Color.White);
                                    Game1.spriteBatch.Draw(Game1.debrisSpriteSheet, Game1.GlobalToLocal(Game1.viewport, new Vector2((float)(4 * Game1.tileSize), (float)(3 * Game1.tileSize + Game1.tileSize / 6))), new Microsoft.Xna.Framework.Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.debrisSpriteSheet, 16, -1, -1)), Color.White);
                                    Game1.spriteBatch.Draw(Game1.debrisSpriteSheet, Game1.GlobalToLocal(Game1.viewport, new Vector2((float)(4 * Game1.tileSize + Game1.tileSize / 5), (float)(2 * Game1.tileSize + Game1.tileSize / 3))), new Microsoft.Xna.Framework.Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.debrisSpriteSheet, 16, -1, -1)), Color.White);
                                }
                                if (Game1.displayFarmer && Game1.player.ActiveObject != null && (Game1.player.ActiveObject.bigCraftable && this.checkBigCraftableBoundariesForFrontLayer()) && Game1.currentLocation.Map.GetLayer("Front").PickTile(new Location(Game1.player.getStandingX(), Game1.player.getStandingY()), Game1.viewport.Size) == null)
                                    Game1.drawPlayerHeldObject(Game1.player);
                                else if (Game1.displayFarmer && Game1.player.ActiveObject != null)
                                {
                                    if (Game1.currentLocation.Map.GetLayer("Front").PickTile(new Location((int)Game1.player.position.X, (int)Game1.player.position.Y - Game1.tileSize * 3 / 5), Game1.viewport.Size) == null || Game1.currentLocation.Map.GetLayer("Front").PickTile(new Location((int)Game1.player.position.X, (int)Game1.player.position.Y - Game1.tileSize * 3 / 5), Game1.viewport.Size).TileIndexProperties.ContainsKey("FrontAlways"))
                                    {
                                        Layer layer1 = Game1.currentLocation.Map.GetLayer("Front");
                                        rectangle = Game1.player.GetBoundingBox();
                                        Location mapDisplayLocation1 = new Location(rectangle.Right, (int)Game1.player.position.Y - Game1.tileSize * 3 / 5);
                                        Size size1 = Game1.viewport.Size;
                                        if (layer1.PickTile(mapDisplayLocation1, size1) != null)
                                        {
                                            Layer layer2 = Game1.currentLocation.Map.GetLayer("Front");
                                            rectangle = Game1.player.GetBoundingBox();
                                            Location mapDisplayLocation2 = new Location(rectangle.Right, (int)Game1.player.position.Y - Game1.tileSize * 3 / 5);
                                            Size size2 = Game1.viewport.Size;
                                            if (layer2.PickTile(mapDisplayLocation2, size2).TileIndexProperties.ContainsKey("FrontAlways"))
                                                goto label_127;
                                        }
                                        else
                                            goto label_127;
                                    }
                                    Game1.drawPlayerHeldObject(Game1.player);
                                }
                                label_127:
                                if ((Game1.player.UsingTool || Game1.pickingTool) && Game1.player.CurrentTool != null && ((!Game1.player.CurrentTool.Name.Equals("Seeds") || Game1.pickingTool) && (Game1.currentLocation.Map.GetLayer("Front").PickTile(new Location(Game1.player.getStandingX(), (int)Game1.player.position.Y - Game1.tileSize * 3 / 5), Game1.viewport.Size) != null && Game1.currentLocation.Map.GetLayer("Front").PickTile(new Location(Game1.player.getStandingX(), Game1.player.getStandingY()), Game1.viewport.Size) == null)))
                                    Game1.drawTool(Game1.player);
                                if (Game1.currentLocation.Map.GetLayer("AlwaysFront") != null)
                                {
                                    Game1.mapDisplayDevice.BeginScene(Game1.spriteBatch);
                                    Game1.currentLocation.Map.GetLayer("AlwaysFront").Draw(Game1.mapDisplayDevice, Game1.viewport, Location.Origin, false, Game1.pixelZoom);
                                    Game1.mapDisplayDevice.EndScene();
                                }
                                if ((double)Game1.toolHold > 400.0 && Game1.player.CurrentTool.UpgradeLevel >= 1 && Game1.player.canReleaseTool)
                                {
                                    Color color = Color.White;
                                    switch ((int)((double)Game1.toolHold / 600.0) + 2)
                                    {
                                        case 1:
                                            color = Tool.copperColor;
                                            break;
                                        case 2:
                                            color = Tool.steelColor;
                                            break;
                                        case 3:
                                            color = Tool.goldColor;
                                            break;
                                        case 4:
                                            color = Tool.iridiumColor;
                                            break;
                                    }
                                    Game1.spriteBatch.Draw(Game1.littleEffect, new Microsoft.Xna.Framework.Rectangle((int)Game1.player.getLocalPosition(Game1.viewport).X - 2, (int)Game1.player.getLocalPosition(Game1.viewport).Y - (Game1.player.CurrentTool.Name.Equals("Watering Can") ? 0 : Game1.tileSize) - 2, (int)((double)Game1.toolHold % 600.0 * 0.0799999982118607) + 4, Game1.tileSize / 8 + 4), Color.Black);
                                    Game1.spriteBatch.Draw(Game1.littleEffect, new Microsoft.Xna.Framework.Rectangle((int)Game1.player.getLocalPosition(Game1.viewport).X, (int)Game1.player.getLocalPosition(Game1.viewport).Y - (Game1.player.CurrentTool.Name.Equals("Watering Can") ? 0 : Game1.tileSize), (int)((double)Game1.toolHold % 600.0 * 0.0799999982118607), Game1.tileSize / 8), color);
                                }
                                if (Game1.isDebrisWeather && Game1.currentLocation.IsOutdoors && (!Game1.currentLocation.ignoreDebrisWeather && !Game1.currentLocation.Name.Equals("Desert")) && Game1.viewport.X > -10)
                                {
                                    foreach (WeatherDebris weatherDebris in Game1.debrisWeather)
                                        weatherDebris.draw(Game1.spriteBatch);
                                }
                                if (Game1.farmEvent != null)
                                    Game1.farmEvent.draw(Game1.spriteBatch);
                                if ((double)Game1.currentLocation.LightLevel > 0.0 && Game1.timeOfDay < 2000)
                                    Game1.spriteBatch.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * Game1.currentLocation.LightLevel);
                                if (Game1.screenGlow)
                                    Game1.spriteBatch.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Game1.screenGlowColor * Game1.screenGlowAlpha);
                                Game1.currentLocation.drawAboveAlwaysFrontLayer(Game1.spriteBatch);
                                if (Game1.player.CurrentTool != null && Game1.player.CurrentTool is FishingRod && ((Game1.player.CurrentTool as FishingRod).isTimingCast || (double)(Game1.player.CurrentTool as FishingRod).castingChosenCountdown > 0.0 || ((Game1.player.CurrentTool as FishingRod).fishCaught || (Game1.player.CurrentTool as FishingRod).showingTreasure)))
                                    Game1.player.CurrentTool.draw(Game1.spriteBatch);
                                if (Game1.isRaining && Game1.currentLocation.IsOutdoors && (!Game1.currentLocation.Name.Equals("Desert") && !(Game1.currentLocation is Summit)) && (!Game1.eventUp || Game1.currentLocation.isTileOnMap(new Vector2((float)(Game1.viewport.X / Game1.tileSize), (float)(Game1.viewport.Y / Game1.tileSize)))))
                                {
                                    for (int index = 0; index < Game1.rainDrops.Length; ++index)
                                        Game1.spriteBatch.Draw(Game1.rainTexture, Game1.rainDrops[index].position, new Microsoft.Xna.Framework.Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.rainTexture, Game1.rainDrops[index].frame, -1, -1)), Color.White);
                                }
                                Game1.spriteBatch.End();
                                //base.Draw(gameTime);
                                Game1.spriteBatch.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                                if (Game1.eventUp && Game1.currentLocation.currentEvent != null)
                                {
                                    foreach (NPC actor in Game1.currentLocation.currentEvent.actors)
                                    {
                                        if (actor.isEmoting)
                                        {
                                            Vector2 localPosition = actor.getLocalPosition(Game1.viewport);
                                            localPosition.Y -= (float)(Game1.tileSize * 2 + Game1.pixelZoom * 3);
                                            if (actor.age == 2)
                                                localPosition.Y += (float)(Game1.tileSize / 2);
                                            else if (actor.gender == 1)
                                                localPosition.Y += (float)(Game1.tileSize / 6);
                                            Game1.spriteBatch.Draw(Game1.emoteSpriteSheet, localPosition, new Microsoft.Xna.Framework.Rectangle?(new Microsoft.Xna.Framework.Rectangle(actor.CurrentEmoteIndex * (Game1.tileSize / 4) % Game1.emoteSpriteSheet.Width, actor.CurrentEmoteIndex * (Game1.tileSize / 4) / Game1.emoteSpriteSheet.Width * (Game1.tileSize / 4), Game1.tileSize / 4, Game1.tileSize / 4)), Color.White, 0.0f, Vector2.Zero, 4f, SpriteEffects.None, (float)actor.getStandingY() / 10000f);
                                        }
                                    }
                                }
                                Game1.spriteBatch.End();
                                if (Game1.drawLighting)
                                {
                                    Game1.spriteBatch.Begin(SpriteSortMode.Deferred, this.lightingBlend, SamplerState.LinearClamp, (DepthStencilState)null, (RasterizerState)null);
                                    Game1.spriteBatch.Draw((Texture2D)Game1.lightmap, Vector2.Zero, new Microsoft.Xna.Framework.Rectangle?(Game1.lightmap.Bounds), Color.White, 0.0f, Vector2.Zero, (float)(Game1.options.lightingQuality / 2), SpriteEffects.None, 1f);
                                    if (Game1.isRaining && Game1.currentLocation.isOutdoors && !(Game1.currentLocation is Desert))
                                        Game1.spriteBatch.Draw(Game1.staminaRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.OrangeRed * 0.45f);
                                    Game1.spriteBatch.End();
                                }
                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                                if (Game1.drawGrid)
                                {
                                    int x1 = -Game1.viewport.X % Game1.tileSize;
                                    float num1 = (float)(-Game1.viewport.Y % Game1.tileSize);
                                    int x2 = x1;
                                    while (x2 < Game1.graphics.GraphicsDevice.Viewport.Width)
                                    {
                                        Game1.spriteBatch.Draw(Game1.staminaRect, new Microsoft.Xna.Framework.Rectangle(x2, (int)num1, 1, Game1.graphics.GraphicsDevice.Viewport.Height), Color.Red * 0.5f);
                                        x2 += Game1.tileSize;
                                    }
                                    float num2 = num1;
                                    while ((double)num2 < (double)Game1.graphics.GraphicsDevice.Viewport.Height)
                                    {
                                        Game1.spriteBatch.Draw(Game1.staminaRect, new Microsoft.Xna.Framework.Rectangle(x1, (int)num2, Game1.graphics.GraphicsDevice.Viewport.Width, 1), Color.Red * 0.5f);
                                        num2 += (float)Game1.tileSize;
                                    }
                                }
                                if (Game1.currentBillboard != 0)
                                    this.drawBillboard();
                                if ((Game1.displayHUD || Game1.eventUp) && (Game1.currentBillboard == 0 && (int)Game1.gameMode == 3) && (!Game1.freezeControls && !Game1.panMode))
                                {
                                    GraphicsEvents.InvokeOnPreRenderHudEvent(this.Monitor);
                                    this.drawHUD();
                                    GraphicsEvents.InvokeOnPostRenderHudEvent(this.Monitor);
                                }
                                else if (Game1.activeClickableMenu == null && Game1.farmEvent == null)
                                    Game1.spriteBatch.Draw(Game1.mouseCursors, new Vector2((float)Game1.getOldMouseX(), (float)Game1.getOldMouseY()), new Microsoft.Xna.Framework.Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 0, 16, 16)), Color.White, 0.0f, Vector2.Zero, (float)(4.0 + (double)Game1.dialogueButtonScale / 150.0), SpriteEffects.None, 1f);
                                if (Game1.hudMessages.Count > 0 && (!Game1.eventUp || Game1.isFestival()))
                                {
                                    for (int i = Game1.hudMessages.Count - 1; i >= 0; --i)
                                        Game1.hudMessages[i].draw(Game1.spriteBatch, i);
                                }
                            }
                            if (Game1.farmEvent != null)
                                Game1.farmEvent.draw(Game1.spriteBatch);
                            if (Game1.dialogueUp && !Game1.nameSelectUp && !Game1.messagePause && (Game1.activeClickableMenu == null || !(Game1.activeClickableMenu is DialogueBox)))
                                this.drawDialogueBox();
                            Viewport viewport;
                            if (Game1.progressBar)
                            {
                                SpriteBatch spriteBatch1 = Game1.spriteBatch;
                                Texture2D fadeToBlackRect = Game1.fadeToBlackRect;
                                int x1 = (Game1.graphics.GraphicsDevice.Viewport.TitleSafeArea.Width - Game1.dialogueWidth) / 2;
                                rectangle = Game1.graphics.GraphicsDevice.Viewport.TitleSafeArea;
                                int y1 = rectangle.Bottom - Game1.tileSize * 2;
                                int dialogueWidth = Game1.dialogueWidth;
                                int height1 = Game1.tileSize / 2;
                                Microsoft.Xna.Framework.Rectangle destinationRectangle1 = new Microsoft.Xna.Framework.Rectangle(x1, y1, dialogueWidth, height1);
                                Color lightGray = Color.LightGray;
                                spriteBatch1.Draw(fadeToBlackRect, destinationRectangle1, lightGray);
                                SpriteBatch spriteBatch2 = Game1.spriteBatch;
                                Texture2D staminaRect = Game1.staminaRect;
                                viewport = Game1.graphics.GraphicsDevice.Viewport;
                                int x2 = (viewport.TitleSafeArea.Width - Game1.dialogueWidth) / 2;
                                viewport = Game1.graphics.GraphicsDevice.Viewport;
                                rectangle = viewport.TitleSafeArea;
                                int y2 = rectangle.Bottom - Game1.tileSize * 2;
                                int width = (int)((double)Game1.pauseAccumulator / (double)Game1.pauseTime * (double)Game1.dialogueWidth);
                                int height2 = Game1.tileSize / 2;
                                Microsoft.Xna.Framework.Rectangle destinationRectangle2 = new Microsoft.Xna.Framework.Rectangle(x2, y2, width, height2);
                                Color dimGray = Color.DimGray;
                                spriteBatch2.Draw(staminaRect, destinationRectangle2, dimGray);
                            }
                            if (Game1.eventUp && Game1.currentLocation != null && Game1.currentLocation.currentEvent != null)
                                Game1.currentLocation.currentEvent.drawAfterMap(Game1.spriteBatch);
                            if (Game1.isRaining && Game1.currentLocation != null && (Game1.currentLocation.isOutdoors && !(Game1.currentLocation is Desert)))
                            {
                                SpriteBatch spriteBatch = Game1.spriteBatch;
                                Texture2D staminaRect = Game1.staminaRect;
                                viewport = Game1.graphics.GraphicsDevice.Viewport;
                                Microsoft.Xna.Framework.Rectangle bounds = viewport.Bounds;
                                Color color = Color.Blue * 0.2f;
                                spriteBatch.Draw(staminaRect, bounds, color);
                            }
                            if ((Game1.fadeToBlack || Game1.globalFade) && !Game1.menuUp && (!Game1.nameSelectUp || Game1.messagePause))
                            {
                                SpriteBatch spriteBatch = Game1.spriteBatch;
                                Texture2D fadeToBlackRect = Game1.fadeToBlackRect;
                                viewport = Game1.graphics.GraphicsDevice.Viewport;
                                Microsoft.Xna.Framework.Rectangle bounds = viewport.Bounds;
                                Color color = Color.Black * ((int)Game1.gameMode == 0 ? 1f - Game1.fadeToBlackAlpha : Game1.fadeToBlackAlpha);
                                spriteBatch.Draw(fadeToBlackRect, bounds, color);
                            }
                            else if ((double)Game1.flashAlpha > 0.0)
                            {
                                if (Game1.options.screenFlash)
                                {
                                    SpriteBatch spriteBatch = Game1.spriteBatch;
                                    Texture2D fadeToBlackRect = Game1.fadeToBlackRect;
                                    viewport = Game1.graphics.GraphicsDevice.Viewport;
                                    Microsoft.Xna.Framework.Rectangle bounds = viewport.Bounds;
                                    Color color = Color.White * Math.Min(1f, Game1.flashAlpha);
                                    spriteBatch.Draw(fadeToBlackRect, bounds, color);
                                }
                                Game1.flashAlpha -= 0.1f;
                            }
                            if ((Game1.messagePause || Game1.globalFade) && Game1.dialogueUp)
                                this.drawDialogueBox();
                            foreach (TemporaryAnimatedSprite overlayTempSprite in Game1.screenOverlayTempSprites)
                                overlayTempSprite.draw(Game1.spriteBatch, true, 0, 0);
                            if (Game1.debugMode)
                            {
                                SpriteBatch spriteBatch = Game1.spriteBatch;
                                SpriteFont smallFont = Game1.smallFont;
                                object[] objArray = new object[10];
                                int index1 = 0;
                                string str1;
                                if (!Game1.panMode)
                                    str1 = "player: " + (object)(Game1.player.getStandingX() / Game1.tileSize) + ", " + (object)(Game1.player.getStandingY() / Game1.tileSize);
                                else
                                    str1 = ((Game1.getOldMouseX() + Game1.viewport.X) / Game1.tileSize).ToString() + "," + (object)((Game1.getOldMouseY() + Game1.viewport.Y) / Game1.tileSize);
                                objArray[index1] = (object)str1;
                                int index2 = 1;
                                string str2 = " mouseTransparency: ";
                                objArray[index2] = (object)str2;
                                int index3 = 2;
                                float cursorTransparency = Game1.mouseCursorTransparency;
                                objArray[index3] = (object)cursorTransparency;
                                int index4 = 3;
                                string str3 = " mousePosition: ";
                                objArray[index4] = (object)str3;
                                int index5 = 4;
                                int mouseX = Game1.getMouseX();
                                objArray[index5] = (object)mouseX;
                                int index6 = 5;
                                string str4 = ",";
                                objArray[index6] = (object)str4;
                                int index7 = 6;
                                int mouseY = Game1.getMouseY();
                                objArray[index7] = (object)mouseY;
                                int index8 = 7;
                                string newLine = Environment.NewLine;
                                objArray[index8] = (object)newLine;
                                int index9 = 8;
                                string str5 = "debugOutput: ";
                                objArray[index9] = (object)str5;
                                int index10 = 9;
                                string debugOutput = Game1.debugOutput;
                                objArray[index10] = (object)debugOutput;
                                string text = string.Concat(objArray);
                                Vector2 position = new Vector2((float)this.GraphicsDevice.Viewport.TitleSafeArea.X, (float)this.GraphicsDevice.Viewport.TitleSafeArea.Y);
                                Color red = Color.Red;
                                double num1 = 0.0;
                                Vector2 zero = Vector2.Zero;
                                double num2 = 1.0;
                                int num3 = 0;
                                double num4 = 0.99999988079071;
                                spriteBatch.DrawString(smallFont, text, position, red, (float)num1, zero, (float)num2, (SpriteEffects)num3, (float)num4);
                            }
                            if (Game1.showKeyHelp)
                                Game1.spriteBatch.DrawString(Game1.smallFont, Game1.keyHelpString, new Vector2((float)Game1.tileSize, (float)(Game1.viewport.Height - Game1.tileSize - (Game1.dialogueUp ? Game1.tileSize * 3 + (Game1.isQuestion ? Game1.questionChoices.Count * Game1.tileSize : 0) : 0)) - Game1.smallFont.MeasureString(Game1.keyHelpString).Y), Color.LightGray, 0.0f, Vector2.Zero, 1f, SpriteEffects.None, 0.9999999f);
                            if (Game1.activeClickableMenu != null)
                            {
                                try
                                {
                                    GraphicsEvents.InvokeOnPreRenderGuiEvent(this.Monitor);
                                    Game1.activeClickableMenu.draw(Game1.spriteBatch);
                                    GraphicsEvents.InvokeOnPostRenderGuiEvent(this.Monitor);
                                }
                                catch (Exception ex)
                                {
                                    this.Monitor.Log($"The {Game1.activeClickableMenu.GetType().FullName} menu crashed while drawing itself. SMAPI will force it to exit to avoid crashing the game.\n{ex.GetLogSummary()}", LogLevel.Error);
                                    Game1.activeClickableMenu.exitThisMenu();
                                }
                            }
                            else if (Game1.farmEvent != null)
                                Game1.farmEvent.drawAboveEverything(Game1.spriteBatch);
                            Game1.spriteBatch.End();
                            if (Game1.overlayMenu != null)
                            {
                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, (DepthStencilState)null, (RasterizerState)null);
                                Game1.overlayMenu.draw(Game1.spriteBatch);
                                Game1.spriteBatch.End();
                            }

                            if (GraphicsEvents.HasPostRenderListeners())
                            {
                                Game1.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
                                GraphicsEvents.InvokeOnPostRenderEvent(this.Monitor);
                                Game1.spriteBatch.End();
                            }

                            this.renderScreenBuffer();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"An error occured in the overridden draw loop: {ex.GetLogSummary()}", LogLevel.Error);
            }
        }

        /****
        ** Methods
        ****/
        /// <summary>Get the controller buttons which are currently pressed.</summary>
        /// <param name="index">The controller to check.</param>
        private Buttons[] GetButtonsDown(PlayerIndex index)
        {
            var state = GamePad.GetState(index);
            var buttons = new List<Buttons>();
            if (state.IsConnected)
            {
                if (state.Buttons.A == ButtonState.Pressed) buttons.Add(Buttons.A);
                if (state.Buttons.B == ButtonState.Pressed) buttons.Add(Buttons.B);
                if (state.Buttons.Back == ButtonState.Pressed) buttons.Add(Buttons.Back);
                if (state.Buttons.BigButton == ButtonState.Pressed) buttons.Add(Buttons.BigButton);
                if (state.Buttons.LeftShoulder == ButtonState.Pressed) buttons.Add(Buttons.LeftShoulder);
                if (state.Buttons.LeftStick == ButtonState.Pressed) buttons.Add(Buttons.LeftStick);
                if (state.Buttons.RightShoulder == ButtonState.Pressed) buttons.Add(Buttons.RightShoulder);
                if (state.Buttons.RightStick == ButtonState.Pressed) buttons.Add(Buttons.RightStick);
                if (state.Buttons.Start == ButtonState.Pressed) buttons.Add(Buttons.Start);
                if (state.Buttons.X == ButtonState.Pressed) buttons.Add(Buttons.X);
                if (state.Buttons.Y == ButtonState.Pressed) buttons.Add(Buttons.Y);
                if (state.DPad.Up == ButtonState.Pressed) buttons.Add(Buttons.DPadUp);
                if (state.DPad.Down == ButtonState.Pressed) buttons.Add(Buttons.DPadDown);
                if (state.DPad.Left == ButtonState.Pressed) buttons.Add(Buttons.DPadLeft);
                if (state.DPad.Right == ButtonState.Pressed) buttons.Add(Buttons.DPadRight);
                if (state.Triggers.Left > 0.2f) buttons.Add(Buttons.LeftTrigger);
                if (state.Triggers.Right > 0.2f) buttons.Add(Buttons.RightTrigger);
            }
            return buttons.ToArray();
        }

        /// <summary>Get the controller buttons which were pressed after the last update.</summary>
        /// <param name="index">The controller to check.</param>
        private Buttons[] GetFramePressedButtons(PlayerIndex index)
        {
            var state = GamePad.GetState(index);
            var buttons = new List<Buttons>();
            if (state.IsConnected)
            {
                if (this.WasButtonJustPressed(Buttons.A, state.Buttons.A, index)) buttons.Add(Buttons.A);
                if (this.WasButtonJustPressed(Buttons.B, state.Buttons.B, index)) buttons.Add(Buttons.B);
                if (this.WasButtonJustPressed(Buttons.Back, state.Buttons.Back, index)) buttons.Add(Buttons.Back);
                if (this.WasButtonJustPressed(Buttons.BigButton, state.Buttons.BigButton, index)) buttons.Add(Buttons.BigButton);
                if (this.WasButtonJustPressed(Buttons.LeftShoulder, state.Buttons.LeftShoulder, index)) buttons.Add(Buttons.LeftShoulder);
                if (this.WasButtonJustPressed(Buttons.LeftStick, state.Buttons.LeftStick, index)) buttons.Add(Buttons.LeftStick);
                if (this.WasButtonJustPressed(Buttons.RightShoulder, state.Buttons.RightShoulder, index)) buttons.Add(Buttons.RightShoulder);
                if (this.WasButtonJustPressed(Buttons.RightStick, state.Buttons.RightStick, index)) buttons.Add(Buttons.RightStick);
                if (this.WasButtonJustPressed(Buttons.Start, state.Buttons.Start, index)) buttons.Add(Buttons.Start);
                if (this.WasButtonJustPressed(Buttons.X, state.Buttons.X, index)) buttons.Add(Buttons.X);
                if (this.WasButtonJustPressed(Buttons.Y, state.Buttons.Y, index)) buttons.Add(Buttons.Y);
                if (this.WasButtonJustPressed(Buttons.DPadUp, state.DPad.Up, index)) buttons.Add(Buttons.DPadUp);
                if (this.WasButtonJustPressed(Buttons.DPadDown, state.DPad.Down, index)) buttons.Add(Buttons.DPadDown);
                if (this.WasButtonJustPressed(Buttons.DPadLeft, state.DPad.Left, index)) buttons.Add(Buttons.DPadLeft);
                if (this.WasButtonJustPressed(Buttons.DPadRight, state.DPad.Right, index)) buttons.Add(Buttons.DPadRight);
                if (this.WasButtonJustPressed(Buttons.LeftTrigger, state.Triggers.Left, index)) buttons.Add(Buttons.LeftTrigger);
                if (this.WasButtonJustPressed(Buttons.RightTrigger, state.Triggers.Right, index)) buttons.Add(Buttons.RightTrigger);
            }
            return buttons.ToArray();
        }

        /// <summary>Get the controller buttons which were released after the last update.</summary>
        /// <param name="index">The controller to check.</param>
        private Buttons[] GetFrameReleasedButtons(PlayerIndex index)
        {
            var state = GamePad.GetState(index);
            var buttons = new List<Buttons>();
            if (state.IsConnected)
            {
                if (this.WasButtonJustReleased(Buttons.A, state.Buttons.A, index)) buttons.Add(Buttons.A);
                if (this.WasButtonJustReleased(Buttons.B, state.Buttons.B, index)) buttons.Add(Buttons.B);
                if (this.WasButtonJustReleased(Buttons.Back, state.Buttons.Back, index)) buttons.Add(Buttons.Back);
                if (this.WasButtonJustReleased(Buttons.BigButton, state.Buttons.BigButton, index)) buttons.Add(Buttons.BigButton);
                if (this.WasButtonJustReleased(Buttons.LeftShoulder, state.Buttons.LeftShoulder, index)) buttons.Add(Buttons.LeftShoulder);
                if (this.WasButtonJustReleased(Buttons.LeftStick, state.Buttons.LeftStick, index)) buttons.Add(Buttons.LeftStick);
                if (this.WasButtonJustReleased(Buttons.RightShoulder, state.Buttons.RightShoulder, index)) buttons.Add(Buttons.RightShoulder);
                if (this.WasButtonJustReleased(Buttons.RightStick, state.Buttons.RightStick, index)) buttons.Add(Buttons.RightStick);
                if (this.WasButtonJustReleased(Buttons.Start, state.Buttons.Start, index)) buttons.Add(Buttons.Start);
                if (this.WasButtonJustReleased(Buttons.X, state.Buttons.X, index)) buttons.Add(Buttons.X);
                if (this.WasButtonJustReleased(Buttons.Y, state.Buttons.Y, index)) buttons.Add(Buttons.Y);
                if (this.WasButtonJustReleased(Buttons.DPadUp, state.DPad.Up, index)) buttons.Add(Buttons.DPadUp);
                if (this.WasButtonJustReleased(Buttons.DPadDown, state.DPad.Down, index)) buttons.Add(Buttons.DPadDown);
                if (this.WasButtonJustReleased(Buttons.DPadLeft, state.DPad.Left, index)) buttons.Add(Buttons.DPadLeft);
                if (this.WasButtonJustReleased(Buttons.DPadRight, state.DPad.Right, index)) buttons.Add(Buttons.DPadRight);
                if (this.WasButtonJustReleased(Buttons.LeftTrigger, state.Triggers.Left, index)) buttons.Add(Buttons.LeftTrigger);
                if (this.WasButtonJustReleased(Buttons.RightTrigger, state.Triggers.Right, index)) buttons.Add(Buttons.RightTrigger);
            }
            return buttons.ToArray();
        }

        /// <summary>Get whether a controller button was pressed since the last check.</summary>
        /// <param name="button">The controller button to check.</param>
        /// <param name="buttonState">The last known state.</param>
        /// <param name="stateIndex">The player whose controller to check.</param>
        private bool WasButtonJustPressed(Buttons button, ButtonState buttonState, PlayerIndex stateIndex)
        {
            return buttonState == ButtonState.Pressed && !this.PreviouslyPressedButtons[(int)stateIndex].Contains(button);
        }

        /// <summary>Get whether a controller button was released since the last check.</summary>
        /// <param name="button">The controller button to check.</param>
        /// <param name="buttonState">The last known state.</param>
        /// <param name="stateIndex">The player whose controller to check.</param>
        private bool WasButtonJustReleased(Buttons button, ButtonState buttonState, PlayerIndex stateIndex)
        {
            return buttonState == ButtonState.Released && this.PreviouslyPressedButtons[(int)stateIndex].Contains(button);
        }

        /// <summary>Get whether an analogue controller button was pressed since the last check.</summary>
        /// <param name="button">The controller button to check.</param>
        /// <param name="value">The last known value.</param>
        /// <param name="stateIndex">The player whose controller to check.</param>
        private bool WasButtonJustPressed(Buttons button, float value, PlayerIndex stateIndex)
        {
            return this.WasButtonJustPressed(button, value > 0.2f ? ButtonState.Pressed : ButtonState.Released, stateIndex);
        }

        /// <summary>Get whether an analogue controller button was released since the last check.</summary>
        /// <param name="button">The controller button to check.</param>
        /// <param name="value">The last known value.</param>
        /// <param name="stateIndex">The player whose controller to check.</param>
        private bool WasButtonJustReleased(Buttons button, float value, PlayerIndex stateIndex)
        {
            return this.WasButtonJustReleased(button, value > 0.2f ? ButtonState.Pressed : ButtonState.Released, stateIndex);
        }

        /// <summary>Detect changes since the last update ticket and trigger mod events.</summary>
        private void UpdateEventCalls()
        {
            // content locale changed event
            if (this.PreviousLocale != LocalizedContentManager.CurrentLanguageCode)
            {
                var oldValue = this.PreviousLocale;
                var newValue = LocalizedContentManager.CurrentLanguageCode;

                if (oldValue != null)
                    ContentEvents.InvokeAfterLocaleChanged(this.Monitor, oldValue.ToString(), newValue.ToString());
                this.PreviousLocale = newValue;
            }

            // save loaded event
            if (Constants.IsSaveLoaded && !SaveGame.IsProcessing/*still loading save*/ && this.AfterLoadTimer >= 0)
            {
                if (this.AfterLoadTimer == 0)
                {
                    SaveEvents.InvokeAfterLoad(this.Monitor);
                    PlayerEvents.InvokeLoadedGame(this.Monitor, new EventArgsLoadedGameChanged(Game1.hasLoadedGame));
                    TimeEvents.InvokeAfterDayStarted(this.Monitor);
                }
                this.AfterLoadTimer--;
            }

            // before exit to title
            if (Game1.exitToTitle)
                this.IsExiting = true;

            // after exit to title
            if (this.IsWorldReady && this.IsExiting && Game1.activeClickableMenu is TitleMenu)
            {
                SaveEvents.InvokeAfterReturnToTitle(this.Monitor);
                this.AfterLoadTimer = 5;
                this.IsExiting = false;
            }

            // input events
            {
                // get latest state
                this.KStateNow = Keyboard.GetState();
                this.MStateNow = Mouse.GetState();
                this.MPositionNow = new Point(Game1.getMouseX(), Game1.getMouseY());

                // raise key pressed
                foreach (var key in this.FramePressedKeys)
                    ControlEvents.InvokeKeyPressed(this.Monitor, key);

                // raise key released
                foreach (var key in this.FrameReleasedKeys)
                    ControlEvents.InvokeKeyReleased(this.Monitor, key);

                // raise controller button pressed
                for (var i = PlayerIndex.One; i <= PlayerIndex.Four; i++)
                {
                    var buttons = this.GetFramePressedButtons(i);
                    foreach (var button in buttons)
                    {
                        if (button == Buttons.LeftTrigger || button == Buttons.RightTrigger)
                            ControlEvents.InvokeTriggerPressed(this.Monitor, i, button, button == Buttons.LeftTrigger ? GamePad.GetState(i).Triggers.Left : GamePad.GetState(i).Triggers.Right);
                        else
                            ControlEvents.InvokeButtonPressed(this.Monitor, i, button);
                    }
                }

                // raise controller button released
                for (var i = PlayerIndex.One; i <= PlayerIndex.Four; i++)
                {
                    foreach (var button in this.GetFrameReleasedButtons(i))
                    {
                        if (button == Buttons.LeftTrigger || button == Buttons.RightTrigger)
                            ControlEvents.InvokeTriggerReleased(this.Monitor, i, button, button == Buttons.LeftTrigger ? GamePad.GetState(i).Triggers.Left : GamePad.GetState(i).Triggers.Right);
                        else
                            ControlEvents.InvokeButtonReleased(this.Monitor, i, button);
                    }
                }

                // raise keyboard state changed
                if (this.KStateNow != this.KStatePrior)
                    ControlEvents.InvokeKeyboardChanged(this.Monitor, this.KStatePrior, this.KStateNow);

                // raise mouse state changed
                if (this.MStateNow != this.MStatePrior)
                {
                    ControlEvents.InvokeMouseChanged(this.Monitor, this.MStatePrior, this.MStateNow, this.MPositionPrior, this.MPositionNow);
                    this.MStatePrior = this.MStateNow;
                    this.MPositionPrior = this.MPositionNow;
                }
            }

            // menu events
            if (Game1.activeClickableMenu != this.PreviousActiveMenu)
            {
                IClickableMenu previousMenu = this.PreviousActiveMenu;
                IClickableMenu newMenu = Game1.activeClickableMenu;

                // raise save events
                // (saving is performed by SaveGameMenu; on days when the player shipping something, ShippingMenu wraps SaveGameMenu)
                if (newMenu is SaveGameMenu || newMenu is ShippingMenu)
                    SaveEvents.InvokeBeforeSave(this.Monitor);
                else if (previousMenu is SaveGameMenu || previousMenu is ShippingMenu)
                {
                    SaveEvents.InvokeAfterSave(this.Monitor);
                    TimeEvents.InvokeAfterDayStarted(this.Monitor);
                }

                // raise menu events
                if (newMenu != null)
                    MenuEvents.InvokeMenuChanged(this.Monitor, previousMenu, newMenu);
                else
                    MenuEvents.InvokeMenuClosed(this.Monitor, previousMenu);

                // update previous menu
                // (if the menu was changed in one of the handlers, deliberately defer detection until the next update so mods can be notified of the new menu change)
                this.PreviousActiveMenu = newMenu;
            }

            // world & player events
            if (this.IsWorldReady)
            {
                // raise location list changed
                if (this.GetHash(Game1.locations) != this.PreviousGameLocations)
                {
                    LocationEvents.InvokeLocationsChanged(this.Monitor, Game1.locations);
                    this.PreviousGameLocations = this.GetHash(Game1.locations);
                }

                // raise current location changed
                if (Game1.currentLocation != this.PreviousGameLocation)
                {
                    LocationEvents.InvokeCurrentLocationChanged(this.Monitor, this.PreviousGameLocation, Game1.currentLocation);
                    this.PreviousGameLocation = Game1.currentLocation;
                }

                // raise player changed
                if (Game1.player != this.PreviousFarmer)
                {
                    PlayerEvents.InvokeFarmerChanged(this.Monitor, this.PreviousFarmer, Game1.player);
                    this.PreviousFarmer = Game1.player;
                }

                // raise player leveled up a skill
                if (Game1.player.combatLevel != this.PreviousCombatLevel)
                {
                    PlayerEvents.InvokeLeveledUp(this.Monitor, EventArgsLevelUp.LevelType.Combat, Game1.player.combatLevel);
                    this.PreviousCombatLevel = Game1.player.combatLevel;
                }
                if (Game1.player.farmingLevel != this.PreviousFarmingLevel)
                {
                    PlayerEvents.InvokeLeveledUp(this.Monitor, EventArgsLevelUp.LevelType.Farming, Game1.player.farmingLevel);
                    this.PreviousFarmingLevel = Game1.player.farmingLevel;
                }
                if (Game1.player.fishingLevel != this.PreviousFishingLevel)
                {
                    PlayerEvents.InvokeLeveledUp(this.Monitor, EventArgsLevelUp.LevelType.Fishing, Game1.player.fishingLevel);
                    this.PreviousFishingLevel = Game1.player.fishingLevel;
                }
                if (Game1.player.foragingLevel != this.PreviousForagingLevel)
                {
                    PlayerEvents.InvokeLeveledUp(this.Monitor, EventArgsLevelUp.LevelType.Foraging, Game1.player.foragingLevel);
                    this.PreviousForagingLevel = Game1.player.foragingLevel;
                }
                if (Game1.player.miningLevel != this.PreviousMiningLevel)
                {
                    PlayerEvents.InvokeLeveledUp(this.Monitor, EventArgsLevelUp.LevelType.Mining, Game1.player.miningLevel);
                    this.PreviousMiningLevel = Game1.player.miningLevel;
                }
                if (Game1.player.luckLevel != this.PreviousLuckLevel)
                {
                    PlayerEvents.InvokeLeveledUp(this.Monitor, EventArgsLevelUp.LevelType.Luck, Game1.player.luckLevel);
                    this.PreviousLuckLevel = Game1.player.luckLevel;
                }

                // raise player inventory changed
                ItemStackChange[] changedItems = this.GetInventoryChanges(Game1.player.items, this.PreviousItems).ToArray();
                if (changedItems.Any())
                {
                    PlayerEvents.InvokeInventoryChanged(this.Monitor, Game1.player.items, changedItems);
                    this.PreviousItems = Game1.player.items.Where(n => n != null).ToDictionary(n => n, n => n.Stack);
                }

                // raise current location's object list changed
                {
                    int? objectHash = Game1.currentLocation?.objects != null ? this.GetHash(Game1.currentLocation.objects) : (int?)null;
                    if (objectHash != null && this.PreviousLocationObjects != objectHash)
                    {
                        LocationEvents.InvokeOnNewLocationObject(this.Monitor, Game1.currentLocation.objects);
                        this.PreviousLocationObjects = objectHash.Value;
                    }
                }

                // raise time changed
                if (Game1.timeOfDay != this.PreviousTime)
                {
                    TimeEvents.InvokeTimeOfDayChanged(this.Monitor, this.PreviousTime, Game1.timeOfDay);
                    this.PreviousTime = Game1.timeOfDay;
                }
                if (Game1.dayOfMonth != this.PreviousDay)
                {
                    TimeEvents.InvokeDayOfMonthChanged(this.Monitor, this.PreviousDay, Game1.dayOfMonth);
                    this.PreviousDay = Game1.dayOfMonth;
                }
                if (Game1.currentSeason != this.PreviousSeason)
                {
                    TimeEvents.InvokeSeasonOfYearChanged(this.Monitor, this.PreviousSeason, Game1.currentSeason);
                    this.PreviousSeason = Game1.currentSeason;
                }
                if (Game1.year != this.PreviousYear)
                {
                    TimeEvents.InvokeYearOfGameChanged(this.Monitor, this.PreviousYear, Game1.year);
                    this.PreviousYear = Game1.year;
                }

                // raise mine level changed
                if (Game1.mine != null && Game1.mine.mineLevel != this.PreviousMineLevel)
                {
                    MineEvents.InvokeMineLevelChanged(this.Monitor, this.PreviousMineLevel, Game1.mine.mineLevel);
                    this.PreviousMineLevel = Game1.mine.mineLevel;
                }
            }

            // raise game day transition event (obsolete)
            if (Game1.newDay != this.PreviousIsNewDay)
            {
                TimeEvents.InvokeOnNewDay(this.Monitor, this.PreviousDay, Game1.dayOfMonth, Game1.newDay);
                this.PreviousIsNewDay = Game1.newDay;
            }
        }

        /// <summary>Get the player inventory changes between two states.</summary>
        /// <param name="current">The player's current inventory.</param>
        /// <param name="previous">The player's previous inventory.</param>
        private IEnumerable<ItemStackChange> GetInventoryChanges(IEnumerable<Item> current, IDictionary<Item, int> previous)
        {
            current = current.Where(n => n != null).ToArray();
            foreach (Item item in current)
            {
                // stack size changed
                if (previous != null && previous.ContainsKey(item))
                {
                    if (previous[item] != item.Stack)
                        yield return new ItemStackChange { Item = item, StackChange = item.Stack - previous[item], ChangeType = ChangeType.StackChange };
                }

                // new item
                else
                    yield return new ItemStackChange { Item = item, StackChange = item.Stack, ChangeType = ChangeType.Added };
            }

            // removed items
            if (previous != null)
            {
                foreach (var entry in previous)
                {
                    if (current.Any(i => i == entry.Key))
                        continue;

                    yield return new ItemStackChange { Item = entry.Key, StackChange = -entry.Key.Stack, ChangeType = ChangeType.Removed };
                }
            }
        }

        /// <summary>Get a hash value for an enumeration.</summary>
        /// <param name="enumerable">The enumeration of items to hash.</param>
        private int GetHash(IEnumerable enumerable)
        {
            var hash = 0;
            foreach (var v in enumerable)
                hash ^= v.GetHashCode();
            return hash;
        }
    }
}
