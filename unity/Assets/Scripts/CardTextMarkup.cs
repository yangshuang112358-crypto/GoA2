#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Goa2.Presentation
{
    public static class CardTextMarkup
    {
        public const string ActionColor="#F6D278",TimingColor="#84D8E7",RestrictionColor="#FFFFFF",DefeatColor="#FF9B94";
        public sealed class Keyword
        {
            public string Name { get; }
            public string Color { get; }
            public string Explanation { get; }
            public string Rule { get; }
            public IReadOnlyList<string> Terms { get; }
            public Keyword(string name,string color,string explanation,string rule,params string[] terms)
            { Name=name;Color=color;Explanation=explanation;Rule=rule;Terms=Array.AsReadOnly(terms); }
        }
        public sealed class Span
        {
            public int Start { get; }
            public int Length { get; }
            public Keyword Keyword { get; }
            public Span(int start,int length,Keyword keyword) { Start=start;Length=length;Keyword=keyword; }
        }
        public static readonly IReadOnlyList<Keyword> Keywords=Array.AsReadOnly(new[]
        {
            new Keyword("攻击",ActionColor,"发起攻击并处理目标的防御或移除/击败。目标、距离和附加步骤以牌文为准。","R-COMBAT","攻击距离","远程攻击","攻击"),
            new Keyword("移动",ActionColor,"普通移动逐格消耗预算，不能穿过或停在障碍及其他单位所在格；持续限制仍适用。","R-MOVE","普通移动","移动"),
            new Keyword("快速移动",ActionColor,"按快速移动的独立合法范围选择目的地；不能拿普通移动范围代替。","R-MOVE","快速移动"),
            new Keyword("防御",ActionColor,"被攻击时从可用手牌选择合法防御；数值防御与牌文抵挡分别判断。","R-DEFENSE","主要防御","次要防御","防御"),
            new Keyword("抵挡",ActionColor,"牌文规定的抵挡效果；可用性取决于本卡条件及本次攻击限制。","R-DEFENSE","抵挡"),
            new Keyword("弃牌",ActionColor,"由指定玩家选择合法牌并移入弃牌区；弃牌本身不执行该牌的主要文字。","R-CARD-ZONES","丢弃","弃牌"),
            new Keyword("取回",ActionColor,"将牌文指定区域中的合法卡牌取回手牌。","R-CARD-ZONES","取回"),
            new Keyword("放置",ActionColor,"按该效果选择合法位置；不能默认使用普通移动的路径规则。","R-MOVE","放置"),
            new Keyword("推动",ActionColor,"按推动的规则和本卡方向、距离处理；遇阻挡可停止。","R-MOVE","推动"),
            new Keyword("交换",ActionColor,"交换卡牌与交换地图位置是不同动作，按本牌指定的对象和区域处理。","R-CARD-ZONES","交换位置","交换"),
            new Keyword("免疫",ActionColor,"对规定行动既不能被指定，也不受其影响；免疫范围以牌文为准。","R-UNITS","免疫"),
            new Keyword("技能范围",ActionColor,"技能或效果的距离，不自动等于远程攻击距离。","R-TARGETS","技能范围"),
            new Keyword("攻击前",TimingColor,"先处理牌文的攻击前步骤；需要玩家选择时暂停，完成后继续原攻击。","R-EFFECTS","攻击前"),
            new Keyword("攻击后",TimingColor,"原攻击结算后继续牌文后续步骤；不自动等同于成功击败后。","R-EFFECTS","攻击后"),
            new Keyword("本回合",TimingColor,"持续到当前四席出牌周期结束；生效前不提前影响行动。","R-ROUND","此回合","本回合"),
            new Keyword("下一回合",TimingColor,"仅指本轮的下一回合；第四回合产生的此类效果不跨到下一轮。","R-ROUND","下一回合","下回合"),
            new Keyword("本轮",TimingColor,"轮与回合不同：一轮包含四个出牌回合。具体清理时点按该效果合同。","R-ROUND","此轮","本轮"),
            new Keyword("可选",RestrictionColor,"由指定选择者决定是否执行；不意味着后续其他步骤都能跳过。","R-EFFECTS","你可以","可以","可选择"),
            new Keyword("条件与义务",RestrictionColor,"条件、必须和替代分支会改变合法行为，必须结合整段牌文及逐卡裁定阅读。","R-EFFECTS","如果可行","若如此做","如果可能","若如此","否则","必须","如果"),
            new Keyword("数量限制",RestrictionColor,"最多、至少等限制约束本次数量；是否允许零个目标仍以逐卡合同为准。","R-EFFECTS","不超过","最多","至少"),
            new Keyword("否定与例外",RestrictionColor,"否定条件与例外都是规则正文；“非远程攻击”不能误读成远程攻击。","R-EFFECTS","不是远程攻击","非远程攻击","非远程","不是","不能","无法","不可","除外","不会","无需"),
            new Keyword("被击败",DefeatColor,"英雄进入正常击败流程，处理奖励、水晶损失、胜利检查及待复活。小兵按其移除规则处理。","R-DEFEAT","被击败","击败"),
            new Keyword("数量与单位",ActionColor,"数值与格、张卡牌等单位一起阅读；被动加成是否适用按该数值和规则判断。","R-EFFECTS")
        });
        private static readonly (string term,Keyword keyword)[] Terms=Keywords.SelectMany(k=>k.Terms.Select(t=>(term:t,keyword:k))).OrderByDescending(t=>t.term.Length).ToArray();
        private static readonly Regex Quantity=new Regex(@"\G(?:[+−-]?\d+|[一二三四五六七八九十两]+)(?:张卡牌|张牌|格|次)");
        private static readonly Regex Tags=new Regex(@"</?(?:b|nobr|color(?:=#[0-9A-Fa-f]{6})?)>");
        private const string LiteralLessThan="<noparse><</noparse>";
        public static IReadOnlyList<Span> Spans(string text)
        {
            var spans=new List<Span>();
            for(int index=0;index<text.Length;)
            {
                var term=Terms.FirstOrDefault(t=>index+t.term.Length<=text.Length && string.CompareOrdinal(text,index,t.term,0,t.term.Length)==0);
                if(term.term!=null) { spans.Add(new Span(index,term.term.Length,term.keyword));index+=term.term.Length;continue; }
                var quantity=Quantity.Match(text,index);
                if(quantity.Success) { spans.Add(new Span(index,quantity.Length,Keywords[Keywords.Count-1]));index+=quantity.Length;continue; }
                index++;
            }
            return spans;
        }
        private static string Escape(string text) => text.Replace("<",LiteralLessThan);
        public static string Format(string text)
        {
            var result=new StringBuilder();int start=0;
            foreach(var span in Spans(text))
            {
                result.Append(Escape(text.Substring(start,span.Start-start)));
                result.Append("<nobr><color=").Append(span.Keyword.Color).Append("><b>").Append(Escape(text.Substring(span.Start,span.Length))).Append("</b></color></nobr>");
                start=span.Start+span.Length;
            }
            return result.Append(Escape(text.Substring(start))).ToString();
        }
        public static string PlainText(string text) => Tags.Replace(text,"").Replace(LiteralLessThan,"<");
        public static string Preview(string text,float width,Func<string,float> measure)
        {
            string remaining=text.Replace("\r","").Replace("\n"," ");
            string TakeLine(bool last)
            {
                int count=0;
                while(count<remaining.Length && measure(Format(remaining.Substring(0,count+1))+(last && count+1<remaining.Length ? "…" : ""))<=width) count++;
                // Prefer a complete negative clause, timing, or quantity over splitting it between lines.
                var split=Spans(remaining).FirstOrDefault(s=>s.Start<count && s.Start+s.Length>count);
                if(split!=null && split.Start>0) count=split.Start;
                count=Math.Max(1,count);count=Math.Min(count,remaining.Length);
                string line=remaining.Substring(0,count);remaining=remaining.Substring(count);
                return Format(line)+(last && remaining.Length>0 ? "…" : "");
            }
            string first=TakeLine(false);
            return first+(remaining.Length==0 ? "" : "\n"+TakeLine(true));
        }
    }
}
