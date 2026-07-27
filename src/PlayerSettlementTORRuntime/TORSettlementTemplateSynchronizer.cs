using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace PlayerSettlementTORRuntime
{
    public sealed class SubModule : MBSubModuleBase
    {
        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            if (!(game.GameType is Campaign)) return;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try { TORSettlementTemplateSynchronizer.Synchronize(); }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print("[PlayerSettlementTORRuntime] Town template synchronization failed: " + ex);
            }
        }
    }

    internal static class TORSettlementTemplateSynchronizer
    {
        private const string PlayerSettlementModule = "PlayerSettlement";
        private const string TorTemplateModule = "PlayerSettlement_TOR";
        private const string TorCoreModule = "TOR_Core";
        private const string RuntimeModifier = "_ToR_runtime";
        private const string Marker = "_ToR";
        private const string PlayerSettlementPrefix = "player_settlement_";

        internal static void Synchronize()
        {
            var torModule = ModuleHelper.GetModuleInfo(TorCoreModule);
            if (torModule == null || string.IsNullOrEmpty(torModule.FolderPath)) return;
            string settlementsPath = Path.Combine(torModule.FolderPath, "ModuleData", "tor_settlements.xml");
            if (!File.Exists(settlementsPath)) return;

            var torSettlements = new XmlDocument();
            torSettlements.Load(settlementsPath);
            var townsByCulture = ReadLiveTorTownsInVisualOrder(torSettlements);
            if (townsByCulture.Count == 0) return;

            Assembly playerSettlement = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, PlayerSettlementModule, StringComparison.OrdinalIgnoreCase));
            Type mainType = playerSettlement?.GetType("BannerlordPlayerSettlement.Main", false);
            object submodule = mainType?.GetField("Submodule", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            IDictionary templates = mainType?.GetField("CultureTemplates", BindingFlags.Public | BindingFlags.Instance)?.GetValue(submodule) as IDictionary;
            if (templates == null) return;

            Type templateType = playerSettlement.GetType("BannerlordPlayerSettlement.Descriptors.CultureSettlementTemplate", true);
            Type listType = typeof(List<>).MakeGenericType(templateType);
            foreach (var entry in townsByCulture)
            {
                if (entry.Value.Count == 0) continue;
                IList list = templates.Contains(entry.Key) ? templates[entry.Key] as IList : null;
                if (list == null)
                {
                    list = (IList)Activator.CreateInstance(listType);
                    templates[entry.Key] = list;
                }
                RemovePriorRuntimeTemplatesAndStaticTorTowns(list);
                object template = Activator.CreateInstance(templateType);
                SetField(templateType, template, "FromModule", TorTemplateModule);
                SetField(templateType, template, "TemplateModifier", RuntimeModifier);
                SetField(templateType, template, "Document", BuildTemplateDocument(entry.Key, entry.Value));
                SetField(templateType, template, "CultureId", entry.Key);
                list.Add(template);
            }
        }

        private static Dictionary<string, List<XmlElement>> ReadLiveTorTownsInVisualOrder(XmlDocument document)
        {
            var xmlById = new Dictionary<string, XmlElement>(StringComparer.OrdinalIgnoreCase);
            XmlNodeList nodes = document.SelectNodes("//Settlement");
            if (nodes != null)
                foreach (XmlElement settlement in nodes.OfType<XmlElement>())
                    if (!string.IsNullOrEmpty(settlement.GetAttribute("id"))) xmlById[settlement.GetAttribute("id")] = settlement;

            var result = new Dictionary<string, List<XmlElement>>(StringComparer.OrdinalIgnoreCase);
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null || !settlement.IsTown || !settlement.IsVisible || settlement.Culture == null) continue;
                if (!string.IsNullOrEmpty(settlement.StringId) && settlement.StringId.StartsWith(PlayerSettlementPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(settlement.StringId) || !xmlById.TryGetValue(settlement.StringId, out XmlElement source)) continue;
                XmlElement town = source.SelectSingleNode("./Components/Town") as XmlElement;
                if (town == null || IsCastle(town) || string.IsNullOrEmpty(settlement.Culture.StringId)) continue;
                if (!result.TryGetValue(settlement.Culture.StringId, out List<XmlElement> list))
                    result[settlement.Culture.StringId] = list = new List<XmlElement>();
                list.Add(source);
            }
            return result;
        }

        private static bool IsCastle(XmlElement town)
        {
            string value = town.GetAttribute("is_castle");
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
        }

        private static void RemovePriorRuntimeTemplatesAndStaticTorTowns(IList templateList)
        {
            for (int i = templateList.Count - 1; i >= 0; i--)
            {
                object template = templateList[i];
                if (template == null) continue;
                Type type = template.GetType();
                if (!string.Equals(type.GetField("FromModule")?.GetValue(template) as string, TorTemplateModule, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(type.GetField("TemplateModifier")?.GetValue(template) as string, RuntimeModifier, StringComparison.OrdinalIgnoreCase))
                {
                    templateList.RemoveAt(i);
                    continue;
                }
                XmlDocument doc = type.GetField("Document")?.GetValue(template) as XmlDocument;
                XmlNodeList towns = doc?.SelectNodes("//Settlement[@template_type='Town']");
                if (towns != null)
                    foreach (XmlNode town in towns.Cast<XmlNode>().ToList()) town.ParentNode?.RemoveChild(town);
                XmlElement root = doc?.DocumentElement;
                if (root != null && root.HasAttribute("towns")) root.SetAttribute("towns", "0");
            }
        }

        private static XmlDocument BuildTemplateDocument(string cultureId, IList<XmlElement> sourceTowns)
        {
            var output = new XmlDocument();
            XmlElement root = output.CreateElement("Settlements");
            output.AppendChild(root);
            root.SetAttribute("towns", sourceTowns.Count.ToString());
            root.SetAttribute("castles", "0");
            root.SetAttribute("villages", "0");
            root.SetAttribute("culture_template", cultureId);
            root.SetAttribute("template_modifier", RuntimeModifier);
            for (int i = 0; i < sourceTowns.Count; i++)
            {
                XmlElement clone = (XmlElement)output.ImportNode(sourceTowns[i], true);
                PrepareTownTemplate(clone, cultureId, i + 1);
                root.AppendChild(clone);
            }
            return output;
        }

        private static void PrepareTownTemplate(XmlElement settlement, string cultureId, int variant)
        {
            string id = "player_settlement_town_" + Sanitize(cultureId) + "_variant_" + variant + Marker + "_runtime";
            settlement.SetAttribute("id", id);
            settlement.SetAttribute("name", "{=player_settlement_n_01}Player Settlement");
            settlement.SetAttribute("owner", "Faction.{{PLAYER_CLAN}}");
            settlement.SetAttribute("posX", "{{POS_X}}");
            settlement.SetAttribute("posY", "{{POS_Y}}");
            settlement.SetAttribute("culture", "Culture.{{PLAYER_CULTURE}}");
            settlement.SetAttribute("gate_posX", "{{G_POS_X}}");
            settlement.SetAttribute("gate_posY", "{{G_POS_Y}}");
            settlement.SetAttribute("template_type", "Town");
            settlement.SetAttribute("template_variant", variant.ToString());
            settlement.RemoveAttribute("prefab_id");
            settlement.RemoveAttribute("port_posX");
            settlement.RemoveAttribute("port_posY");
            var town = settlement.SelectSingleNode("./Components/Town") as XmlElement;
            if (town != null) { town.SetAttribute("id", id + "_town_comp"); town.SetAttribute("is_castle", "false"); }
        }

        private static string Sanitize(string value) => string.IsNullOrEmpty(value)
            ? "tor"
            : new string(value.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());

        private static void SetField(Type type, object target, string name, object value)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            field.SetValue(target, value);
        }
    }
}
