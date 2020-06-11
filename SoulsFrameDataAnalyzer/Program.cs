using System;

using SoulsFormats;
using SFAnimExtensions;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using YamlDotNet.Core.Tokens;

namespace SoulsFrameDataAnalyzer
{
	class AttackInfo : IComparable
	{
		public string animName;
		public List<(float attackStart, float attackEnd)> Attacks = new List<(float attackStart, float attackEnd)>();
		internal float RecoveryStart = -1;
		internal string details;

		public TAE.Animation anim { get; internal set; }

		public int CompareTo(object obj)
		{
			AttackInfo other = (AttackInfo)obj;
			return animName.CompareTo(other.animName);

			//if (RecoveryStart == other.RecoveryStart)
			{
				if (Attacks.Count > 0 && other.Attacks.Count > 0)
				{
					return Attacks[0].attackStart.CompareTo(other.Attacks[0].attackStart);
				}
			}

			return RecoveryStart.CompareTo(other.RecoveryStart);
		}
	}

	class TAEAnalyzerData
	{
		public class AnimData
		{
			public string Regex { get; private set; }
			public IDictionary<string, string> Details { get; private set; }

			private IDictionary<Regex, string> cachedRegexToDetails;

			public bool TryGetDetails(string animDataName, TAE.Animation anim, out string details)
			{
				if (cachedRegexToDetails == null)
				{
					cachedRegexToDetails = (Details == null ? (IEnumerable<KeyValuePair<string, string>>) new KeyValuePair<string, string>[0] { } : Details).Concat(Regex == null ? new KeyValuePair<string, string>[0] { } : new[] { new KeyValuePair<string, string>(Regex, animDataName) }).ToDictionary(kvp => new Regex(kvp.Key), kvp => kvp.Value);
				}

				foreach(var kvp in cachedRegexToDetails)
				{
					if (kvp.Key.IsMatch(anim.ID.ToString()))
					{
						details = kvp.Value;
						return true;
					}
				}

				details = "not found";

				return false;
			}
		}

		public IDictionary<string, string> weapons { get; private set; }

		public IDictionary<string, AnimData> animKind { get; private set; }
	}

	class Program
	{
		static void Main(string[] args)
		{
			const string basePath = @"D:\Program Files (x86)\Steam\steamapps\common\DARK SOULS III\Game";

			const string actionSetupPath = "Res/TaeToWeaponKind.yml";

			TAEAnalyzerData analyzerData = new YamlDotNet.Serialization.Deserializer().Deserialize<TAEAnalyzerData>(new System.IO.StreamReader(actionSetupPath));

			var reader = new BND4Reader(System.IO.Path.Combine(basePath, "chr/c0000.anibnd.dcx"));


			const int jumpTableEventType = 0;

			const int attackEventType = 1;

			int[] comboBehSubIds = new[] { 4, 116 };

			var taeTemplate = TAE.Template.ReadXMLFile(@"Res/TAE.Template.DS3.xml");

			var jumpTableEventTemplate = taeTemplate[21][jumpTableEventType];
			var attackEventTemplate = taeTemplate[21][attackEventType];

			var analyzedWeapons = analyzerData.weapons.Select(weaponKvp => (kvp: weaponKvp, taeFileHeader: reader.Files
					// get all TAE headers
					.Where(header => header.Name.EndsWith("tae"))
					.Single(header => System.IO.Path.GetFileName(header.Name) == $"a{weaponKvp.Key}.tae")))
				.Select(weaponData =>( weaponData: weaponData, fileContents: reader.ReadFile(weaponData.taeFileHeader)))
				.Where(weaponData=>TAE.Is(weaponData.fileContents))
					.Select((weaponData) => (kvp: weaponData.weaponData.kvp, weaponTae: TAE.Read(weaponData.fileContents)))
					.SelectMany(myTuple => analyzerData.animKind.Select(animKindKvp => (weaponKindKvp: myTuple.kvp, animKindKvp: animKindKvp, weaponTae: myTuple.weaponTae)))
					.SelectMany(myTuple =>
					{
						var attackTimings = myTuple.weaponTae.Animations.Select(anim => {

						bool didFind = myTuple.animKindKvp.Value.TryGetDetails(myTuple.animKindKvp.Key, anim, out string details);

							return (found: didFind, myTuple: myTuple, anim, details); })
						.Where(d =>d.found)
							.Select(anim => (anim, events: anim.anim.Events.Where(taeEvent => taeEvent.Type == attackEventType || taeEvent.Type == jumpTableEventType)))
							.Where(events => events.events.Any())
							.Select(anim => anim.events.Aggregate(new AttackInfo(), (attackInfo, taeEvent) =>
							{
								attackInfo.anim = anim.anim.anim;
								attackInfo.details = anim.anim.details;
								if (taeEvent.Type == attackEventType)
								{
									taeEvent.ApplyTemplate(false, attackEventTemplate);
									if (taeEvent.Parameters["Unk04"].Equals(0))
									{
										attackInfo.Attacks.Add((attackStart: taeEvent.StartTime, attackEnd: taeEvent.EndTime));
									}
								}

								if (taeEvent.Type == jumpTableEventType)
								{
									taeEvent.ApplyTemplate(false, jumpTableEventTemplate);
									foreach (var comboBenJumpTableId in comboBehSubIds)
									{
										if (taeEvent.Parameters["JumpTableID"].Equals(comboBenJumpTableId))
										{
											attackInfo.RecoveryStart = taeEvent.StartTime;
											break;
										}
									}
								}

								return attackInfo;
							}));

						return attackTimings.Select(attackInfo => (data: myTuple, attackInfo: attackInfo));
					});

			System.IO.TextWriter output = new System.IO.StreamWriter("out.csv");

			foreach (var animKindData in analyzedWeapons.ToLookup(data => data.data.animKindKvp.Key)) //.OrderBy(data=>data.Key))
			{
				output.WriteLine(animKindData.Key);

				foreach (var weaponKindData in animKindData.ToLookup(data => data.data.weaponKindKvp.Value).OrderBy(weaponKindData => weaponKindData.FirstOrDefault().attackInfo?.RecoveryStart))
				{
					if (!ShouldExportWeapon(weaponKindData.Key))
					{
						continue;
					}

					output.WriteLine($",{weaponKindData.Key}");

					foreach (var animData in weaponKindData)
					{

						output.WriteLine($",,{animData.attackInfo.anim.ID},{animData.attackInfo.anim.AnimFileName},{animData.attackInfo.details},{animData.attackInfo.Attacks.OrderBy(attack=> attack.attackStart).Aggregate("", (currentString, attackData) => currentString += attackData.attackStart + "," + attackData.attackEnd + ",")}{animData.attackInfo.RecoveryStart}");
					}
				}
			}
		}

		private static bool ShouldExportWeapon(string weaponKind)
		{
			return weaponKind != "IDK" && !weaponKind.StartsWith("NoAnim") && !weaponKind.StartsWith("Unk");
		}
	}
}
