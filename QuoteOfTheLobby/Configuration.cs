using Dalamud;
using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace QuoteOfTheLobby {
    [Serializable]
    public class Configuration : IPluginConfiguration {
        public int Version { get; set; } = 0;

        public enum TextLayerType {
            RandomDialogue,
            DefaultDatacenter,
            FixedText,
        }

        public enum VerticalSnapType {
            Top = 0,
            Middle = 1,
            Bottom = 2,
        }

        [Serializable]
        public class TextLayerConfiguration {
            public Guid Guid { get; init; } = Guid.NewGuid();

            public string Name = "Unnamed";

            public string VisibleWith = "Title";

            public int FontIndex = 2;

            public float VerticalPosition = 0.7f;

            public float HorizontalMargin = 0.1f;

            public int BackgroundPadding = 2;

            public Vector4 ColorFill = new(1, 1, 1, 1);

            public Vector4 ColorBorder = new(0, 0, 0, 1);

            public Vector4 ColorBackground = new(0, 0, 0, 0);

            public float BorderWidth = 2;

            public float BorderStrength = 1;

            public float FadeDuration = 3f;

            public int CycleInterval = 0;

            public int LanguageVal = Enum.GetNames(typeof(ClientLanguage)).Length;
            public ClientLanguage? Language {
                get {
                    if (LanguageVal == Enum.GetNames(typeof(ClientLanguage)).Length)
                        return null;
                    return (ClientLanguage)LanguageVal;
                }
            }

            public int TypeVal = (int)TextLayerType.RandomDialogue;
            public TextLayerType Type {
                get { return (TextLayerType)TypeVal; }
                set { TypeVal = (int)value; }
            }

            public int HorizontalAlignmentInt = (int)Fdt.LayoutBuilder.HorizontalAlignment.Center;
            public Fdt.LayoutBuilder.HorizontalAlignment HorizontalAlignment {
                get { return (Fdt.LayoutBuilder.HorizontalAlignment)HorizontalAlignmentInt; }
                set { HorizontalAlignmentInt = (int)value; }
            }

            public int VerticalSnapInt = (int)VerticalSnapType.Middle;
            public VerticalSnapType VerticalSnap {
                get { return (VerticalSnapType)VerticalSnapInt; }
                set { VerticalSnapInt = (int)value; }
            }

            public string FixedText = "Text";
        }

        public bool ConfigVisible = true;

        public List<TextLayerConfiguration> TextLayers { get; set; } = new();

        // the below exist just to make saving less cumbersome

        [NonSerialized]
        private DalamudPluginInterface? _pluginInterface;

        public void Initialize(DalamudPluginInterface pluginInterface) {
            _pluginInterface = pluginInterface;
        }

        public void Save() {
            _pluginInterface!.SavePluginConfig(this);
        }
    }
}
