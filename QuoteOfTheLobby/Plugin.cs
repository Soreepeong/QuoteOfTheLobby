using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace QuoteOfTheLobby
{
    public unsafe sealed class Plugin : IDalamudPlugin {
        public string Name => "Quote of the Lobby";

        private readonly DalamudPluginInterface _pluginInterface;
        private readonly Configuration _config;
        private readonly GameResourceReader _reader;
        private readonly VisibilityManager _visibilityManager;

        private readonly Dictionary<Guid, TextLayer> _layers = new();
        private readonly List<IDisposable> _disposableList = new();

        private IntPtr _gameWindowHwnd = IntPtr.Zero;

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] ICommandManager commandManager,
            [RequiredVersion("1.0")] IDataManager dataManager,
            [RequiredVersion("1.0")] IClientState clientState,
            [RequiredVersion("1.0")] ISigScanner sigScanner,
            [RequiredVersion("1.0")] IGameInteropProvider gameInteropProvider) {
            try {
                _pluginInterface = pluginInterface;

                _config = _pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
                _config.Initialize(_pluginInterface);

                _reader = new(dataManager, clientState);
                _disposableList.Add(_visibilityManager = new(sigScanner, gameInteropProvider));

                if (_config.TextLayers.Count == 0)
                    SetupDefaultTextLayers();
                foreach (var t in _config.TextLayers)
                    _layers[t.Guid] = new(_reader, pluginInterface.UiBuilder, t);

                _pluginInterface.UiBuilder.Draw += DrawUI;
                _pluginInterface.UiBuilder.OpenConfigUi += () => { _config.ConfigVisible = !_config.ConfigVisible; };
            } catch {
                Dispose();
                throw;
            }
        }

        private void SetupDefaultTextLayers() {
            _config.TextLayers.Add(new Configuration.TextLayerConfiguration() {
                Name = "Welcome",
                Type = Configuration.TextLayerType.StickyNote,
                VerticalPosition = 0.2f,
                BoldWeight = 1f,
                ItalicWidth = 4f,
                BorderWidth = 4f,
                FontIndex = 15,
                StickyNote = (
                    "**Quote of the Lobby** is now ready.\n" +
                    "Refer to the *config window* to get started."
                ),
            });
            _config.TextLayers.Add(new Configuration.TextLayerConfiguration() {
                Name = "Default Datacenter",
                Type = Configuration.TextLayerType.DefaultDatacenter,
                VerticalPosition = 0.95f,
                FontIndex = 12,
            });
            _config.TextLayers.Add(new Configuration.TextLayerConfiguration() {
                Name = "Random Quote",
                Type = Configuration.TextLayerType.RandomDialogue,
                VerticalPosition = 0.02f,
                FontIndex = 3,
            });
        }

        private void Save() {
            if (_config == null)
                return;

            _config.TextLayers.Clear();
            foreach (var e in _layers.Values)
                _config.TextLayers.Add(e.Config);
            _config.Save();
        }

        public void Dispose() {
            Save();
            foreach (var layer in _layers.Values)
                layer.Dispose();
            _layers.Clear();
            foreach (var item in _disposableList.AsEnumerable().Reverse()) {
                try {
                    item.Dispose();
                } catch (Exception e) {
                    PluginLog.Warning(e, "Dispose failure");
                }
            }
            _disposableList.Clear();
        }

        private void DrawUI() {
            if (_gameWindowHwnd == IntPtr.Zero) {
                while (IntPtr.Zero != (_gameWindowHwnd = Native.FindWindowEx(IntPtr.Zero, _gameWindowHwnd, "FFXIVGAME", null))) {
                    Native.GetWindowThreadProcessId(_gameWindowHwnd, out var pid);
                    if (pid == Environment.ProcessId && Native.IsWindowVisible(_gameWindowHwnd)) break;
                }

                if (_gameWindowHwnd == IntPtr.Zero)
                    return;
            }

            Native.GetClientRect(_gameWindowHwnd, out Native.RECT rc);
            Native.ClientToScreen(_gameWindowHwnd, ref rc);

            ImGui.SetNextWindowSize(new Vector2(400, 640), ImGuiCond.Once);

            if (_config.ConfigVisible) {
                if (ImGui.Begin("Quote of the Lobby Config", ref _config.ConfigVisible))
                    try {
                        if (ImGui.Button("Add")) {
                            var layer = new TextLayer(_reader, _pluginInterface.UiBuilder);
                            _layers[layer.Config.Guid] = layer;
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Shuffle All")) {
                            foreach (var e in _layers.Values)
                                e.RefreshText();
                        }
                        ImGui.SameLine();
                        ImGui.Checkbox("Ignore Visible With", ref _config.ForceShowAllLayers);
                        ImGui.BeginTabBar("Text Layers", ImGuiTabBarFlags.AutoSelectNewTabs);
                        try {
                            SortedSet<Guid>? dels = null;
                            foreach (var e in _layers.Values) {
                                if (!ImGui.BeginTabItem($"{e.Config.Name}###{e.Config.Guid}"))
                                    continue;
                                try {
                                    e.DrawConfigElements();
                                    if (e.Deleted) {
                                        if (dels == null)
                                            dels = new SortedSet<Guid>();
                                        dels.Add(e.Config.Guid);
                                    }
                                } finally {
                                    ImGui.EndTabItem();
                                }
                            }
                            if (dels != null) {
                                foreach (var d in dels) {
                                    _layers[d].Dispose();
                                    _layers.Remove(d);
                                }
                            }
                        } finally { ImGui.EndTabBar(); }
                    } finally { ImGui.End(); }
                else {
                    Save();
                }
            }

            try {
                foreach (var e in _layers.Values) {
                    if (e.Config.VisibleWith == ""
                        || e.Config.VisibleWith.Split(",").Any(x => _visibilityManager.IsVisible(x.Trim()))
                        || _config.ForceShowAllLayers)
                        e.DrawText(rc);
                    else
                        e.RefreshText(true);
                }
            } catch (Exception ex) {
                PluginLog.Error(ex, "?");
            }
        }

    }
}
