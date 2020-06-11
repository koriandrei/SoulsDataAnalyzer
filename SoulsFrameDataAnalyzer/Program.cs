using System;

using SoulsFormats;
using SFAnimExtensions;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace SoulsFrameDataAnalyzer
{
    class AttackInfo : IComparable
    {
        public string animName;
        public List<(float attackStart, float attackEnd)> Attacks = new List<(float attackStart, float attackEnd)>();
		internal float RecoveryStart;

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
        public IDictionary<string, string> weapons { get; private set; }

        public IDictionary<string, string> animKind { get; private set; }
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

            int[] comboBehSubIds = new[]{ 4, 116 };

            var taeTemplate = TAE.Template.ReadXMLFile(@"Res/TAE.Template.DS3.xml");

            var jumpTableEventTemplate = taeTemplate[21][jumpTableEventType];
            var attackEventTemplate = taeTemplate[21][attackEventType];

            foreach (var weaponKvp in analyzerData.weapons)
            {
                var taeHeader = reader.Files
                    // get all TAE headers
                    .Where(header => header.Name.EndsWith("tae"))
                    .Single(header => System.IO.Path.GetFileName(header.Name) == $"a{weaponKvp.Key}.tae");

                Console.WriteLine($"Weapon {weaponKvp.Value}");

                var tae = TAE.Read(reader.ReadFile(taeHeader));

                foreach (var animationKvp in analyzerData.animKind)
                {
                    var regex = new Regex($@"a..._{animationKvp.Value}\.hkt");

                    var attackTimings = tae.Animations.Where(anim => regex.IsMatch(anim.AnimFileName))
                        .Select(anim => (anim, events: anim.Events.Where(taeEvent => taeEvent.Type == attackEventType || taeEvent.Type == jumpTableEventType)))
                        .Where(events => events.events.Any())
                        .Select(anim => anim.events.Aggregate(new AttackInfo(), (attackInfo, taeEvent) =>
                        {
                            attackInfo.animName = anim.anim.AnimFileName;
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
                        }))
                       .ToArray()
                       ;

                    if (attackTimings.Length == 0)
					{
                        continue;
					}

                    Array.Sort(attackTimings);

                    Console.WriteLine($"{animationKvp.Key}");

                    foreach (var item in attackTimings)
                    {
                        Console.Write($"{item.animName}\t");

                        foreach (var timing in item.Attacks)
                        {
                            Console.Write($"{timing.attackStart}\t{timing.attackEnd}\t");
                        }

                        Console.Write($"{item.RecoveryStart}");

                        Console.WriteLine();
                    }

                    Console.WriteLine();

                }

            }
        }
    }
}
