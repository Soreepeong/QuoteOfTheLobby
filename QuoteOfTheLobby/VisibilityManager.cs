using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace QuoteOfTheLobby {
    public unsafe class VisibilityManager : IDisposable {
        private static readonly string HideNamedUiElementSignature = "40 57 48 83 EC 20 48 8B F9 48 8B 89 C8 00 00 00 48 85 C9 0F ?? ?? ?? ?? ?? 8B 87 B0 01 00 00 C1 E8 07 A8 01";
        private static readonly string ShowNamedUiElementSignature = "40 53 48 83 EC 40 48 8B 91 C8 00 00 00 48 8B D9 48 85 D2";
        private const int UnitListCount = 18;
        private delegate AtkStage* GetAtkStageSingleton();
        private const int UnitBaseFlag_Visible = 0x20;

        private delegate IntPtr HideShowNamedUiElementDelegate(IntPtr pThis);
        private readonly Hook<HideShowNamedUiElementDelegate> _hideHook, _showHook;

        private readonly Dictionary<string, bool> _gameLayerVisibility = new();

        private readonly List<IDisposable> _disposableList = new();

        public VisibilityManager(SigScanner sigScanner) {
            try {
                var getSingletonAddr = sigScanner.ScanText("E8 ?? ?? ?? ?? 41 B8 01 00 00 00 48 8D 15 ?? ?? ?? ?? 48 8B 48 20 E8 ?? ?? ?? ?? 48 8B CF");
                var stage = Marshal.GetDelegateForFunctionPointer<GetAtkStageSingleton>(getSingletonAddr)();

                var unitManagers = &stage->RaptureAtkUnitManager->AtkUnitManager.DepthLayerOneList;
                for (var i = 0; i < UnitListCount; i++) {
                    var unitManager = &unitManagers[i];
                    var unitBaseArray = &unitManager->AtkUnitEntries;
                    for (var j = 0; j < unitManager->Count; j++) {
                        var unitBase = unitBaseArray[j];
                        var name = Marshal.PtrToStringAnsi(new IntPtr(unitBase->Name));
                        if (name == null)
                            continue;
                        _gameLayerVisibility[name] = 0 != (unitBase->Flags & UnitBaseFlag_Visible);
                    }
                }

                var hideNamedUiElementAddress = sigScanner.ScanText(HideNamedUiElementSignature);
                var showNamedUiElementAddress = sigScanner.ScanText(ShowNamedUiElementSignature);

                _disposableList.Add(_hideHook = new Hook<HideShowNamedUiElementDelegate>(hideNamedUiElementAddress, this.HideNamedUiElementDetour));
                _disposableList.Add(_showHook = new Hook<HideShowNamedUiElementDelegate>(showNamedUiElementAddress, this.ShowNamedUiElementDetour));
                _hideHook.Enable();
                _showHook.Enable();

            } catch {
                Dispose();
                throw;
            }
        }

        public void Dispose() {
            foreach (var item in _disposableList.AsEnumerable().Reverse()) {
                try {
                    item.Dispose();
                } catch (Exception e) {
                    PluginLog.Warning(e, "Dispose failure");
                }
            }
            _disposableList.Clear();
        }

        private unsafe IntPtr ShowNamedUiElementDetour(IntPtr pThis) {
            var res = _showHook.Original(pThis);
            var windowName = Marshal.PtrToStringUTF8(pThis + 8)!;
            PluginLog.Debug($"Show: {windowName} from {pThis}");
            _gameLayerVisibility[windowName] = true;
            return res;
        }

        private unsafe IntPtr HideNamedUiElementDetour(IntPtr pThis) {
            var res = _hideHook.Original(pThis);
            var windowName = Marshal.PtrToStringUTF8(pThis + 8)!;
            PluginLog.Debug($"Hide: {windowName} from {pThis}");
            _gameLayerVisibility[windowName] = false;
            return res;
        }

        public bool IsVisible(string name) {
            return _gameLayerVisibility.GetValueOrDefault(name, false);
        }
    }
}
