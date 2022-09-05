using BepInEx;
using BepInEx.Configuration;
using R2API;
using R2API.Utils;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Wayfaring2
{
    [BepInDependency(R2API.R2API.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Wayfaring2 : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "GiGaGon";
        public const string PluginName = "Wayfaring2";
        public const string PluginVersion = "2.0.0";

        internal class ModConfig
        {
            public static ConfigEntry<KeyboardShortcut> reloadKeyBind;
            public static ConfigEntry<int> stagesForLoop;
            public static ConfigEntry<int> bracketLengthDefault;
            public static ConfigEntry<string> brackets;

            public static void InitConfig(ConfigFile config)
            {
                reloadKeyBind = config.Bind("General", "Reload Button", new KeyboardShortcut(KeyCode.F6), "Button to reload the stage list with new values from config");
                stagesForLoop = config.Bind("General", "Stages For Loop", 5, "How many stages before the game considers the run to have \"looped\"");
                bracketLengthDefault = config.Bind("General", "Default Bracket Length", 1, "Defualt for how many stages to choose per bracket. Set to -1 for all stages in every bracket");
                brackets = config.Bind("General", "Brackets",
                    "blackbeach,golemplains,snowyforest-" +
                    "goolake,foggyswamp,ancientloft-" +
                    "frozenwall,wispgraveyard,sulfurpools-" +
                    "dampcavesimple,shipgraveyard,rootjungle-" +
                    "skymeadow", "Default is a normal ror2 game. Explained on mod website.");
            }
        }

        internal class Rng
        {
            public static System.Random rng = new();
        }


        public void Awake()
        {
            //init config
            ModConfig.InitConfig(Config);

            Brackets.SetBrackets(ModConfig.brackets.Value);

            string next_stage = "";
            bool calling_from_OnServerSceneChanged = false;

            //hook start of run
            On.RoR2.Run.Start += (orig, self) =>
            {
                //seed rand by stage seed for consistent behavior
                Rng.rng = new((int)Run.instance.seed);

                //set stages per loop from config
                Run.stagesPerLoop = ModConfig.stagesForLoop.Value;

                Brackets.ChooseStages(Brackets.brackets);

                next_stage = Brackets.stages.Pop();

                if (Brackets.stages.Count == 0) Brackets.ChooseStages(Brackets.brackets);


                //randomly decide between 1st stage variants since it doen't happen naturally
                if (next_stage == "blackbeach" && Rng.rng.Next(1) == 1) next_stage = "blackbeach2";
                if (next_stage == "golemplains" && Rng.rng.Next(1) == 1) next_stage = "golemplains2";
                //call origional function
                orig(self);
            };


            //hook picking next stage
            On.RoR2.Run.PickNextStageScene += (orig, self, choices) =>
            {
                orig(self, choices);
                if (calling_from_OnServerSceneChanged)
                {
                    next_stage = Brackets.stages.Pop();

                    if (Brackets.stages.Count == 0) Brackets.ChooseStages(Brackets.brackets);


                    //randomly decide between 1st stage variants since it doen't happen naturally
                    if (next_stage == "blackbeach" && Rng.rng.Next(1) == 1) next_stage = "blackbeach2";
                    if (next_stage == "golemplains" && Rng.rng.Next(1) == 1) next_stage = "golemplains2";

                    calling_from_OnServerSceneChanged = false;
                }
                Run.instance.nextStageScene = SceneCatalog.FindSceneDef(next_stage);
            };

            On.RoR2.Run.OnServerSceneChanged += (orig, self, sceneName) =>
            {
                orig(self, sceneName);
                if (TeleporterInteraction.instance == null)
                {
                    calling_from_OnServerSceneChanged = true;
                    Run.instance.PickNextStageScene(new WeightedSelection<SceneDef> { });
                    Debug.Log("Wayfaring2 - thing happened");
                }
            };

            On.RoR2.BazaarController.SetUpSeerStations += (orig, self) =>
            {
                foreach (SeerStationController controller in self.seerStations)
                {
                    controller.GetComponent<PurchaseInteraction>().SetAvailable(false);
                }
            };
        }
        private void Update()
        {
            if (ModConfig.reloadKeyBind.Value.IsDown())
            {
                Config.Reload();
                Brackets.SetBrackets(ModConfig.brackets.Value);
                Brackets.ChooseStages(Brackets.brackets);
            }
        }

        //using a class to support in run changes
        internal class Brackets
        {
            //init field for later use
            public static List<Tuple<int, List<string>>> brackets = new();
            public static Stack<string> stages = new();

            //formating as list for iteration, int for stage choose amount and list<string> for stages
            public static void SetBrackets(string value)
            {
                //clear brackets for in run changing
                brackets.Clear();
                ////split into brackets by - and iterate over them 
                foreach (string a in value.Split('-'))
                {
                    //check if the defualt stage chose amount was overridden
                    if (a.Contains(':')) {
                        //split off the choose amount from the stages
                        string[] spl = a.Split(':');
                        //format into tuple<int, list<string>> and add to output
                        brackets.Add(new(int.Parse(spl[0]), spl[1].Split(',').ToList()));
                    }
                    else
                    {
                        //define the default amount because I need to use it twice
                        int deflen = ModConfig.bracketLengthDefault.Value;

                        //split into stages
                        string[] spl2 = a.Split(',');
                        //format and add to output
                        brackets.Add(new(deflen != -1 ? deflen : spl2.Length, spl2.ToList()));
                    }
                }
            }

            public static void ChooseStages(List<Tuple<int, List<string>>> value)
            {
                stages.Clear();
                foreach (Tuple<int, List<string>> tuple in value)
                {
                    List<string> x = tuple.Item2.Shuffle().Take(tuple.Item1).ToList();
                    foreach (string s in x)
                    {
                        stages.Push(s);
                    }
                }
                stages = new(stages);
            }
        }
    }

    internal static class ShuffleHelper
    {
        public static List<T> Shuffle<T>(this List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = Wayfaring2.Rng.rng.Next(n + 1);
                (list[n], list[k]) = (list[k], list[n]);
            }
            return list;
        }
    }
}
