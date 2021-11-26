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

            public Vector4 ColorFill = new(255, 255, 255, 255);

            public Vector4 ColorBorder = new(0, 0, 0, 255);

            public int BorderWidth = 1;

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
        }

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
