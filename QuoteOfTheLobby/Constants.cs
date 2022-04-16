using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuoteOfTheLobby {
	public class Constants {
        public static readonly int[] TextureChannelOrder = { 2, 1, 0, 3 };
        public static readonly string[] FontNames = {
			"AXIS_96", "AXIS_12", "AXIS_14", "AXIS_18", "AXIS_36",
			"Jupiter_16", "Jupiter_20", "Jupiter_23", "Jupiter_46",
			"MiedingerMid_10", "MiedingerMid_12", "MiedingerMid_14", "MiedingerMid_18", "MiedingerMid_36",
			"TrumpGothic_184", "TrumpGothic_23", "TrumpGothic_34", "TrumpGothic_68",
		};
		public static readonly string[] HorizontalAlignmentNames = {
			"Left", "Center", "Right",
		};
		public static readonly string[] VerticalSnapNames = {
			"Top", "Middle", "Bottom",
		};
		public static readonly string[] TextLayerTypeNames = {
			"Random Quote", "Default Datacenter", "Sticky Note",
		};
		public static readonly string[] LanguageNames = { 
			"Japanese",
			"English",
			"German",
			"French",
			"Follow client startup language",
		};
    }
}
