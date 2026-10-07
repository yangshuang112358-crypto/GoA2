"""Draw the implemented network, with known input gaps. No generated game imagery."""
import argparse
import json
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import FancyBboxPatch, FancyArrowPatch
from matplotlib.font_manager import FontProperties

p = argparse.ArgumentParser()
p.add_argument("--audit", type=Path, required=True)
p.add_argument("--output", type=Path, required=True)
args = p.parse_args()
r = json.loads(args.audit.read_text(encoding="utf-8"))
assert r["model_shape"] == dict(state_dim=1805, action_dim=242, hidden=64)
assert r["complete"] is False
args.output.mkdir(parents=True, exist_ok=True)
font = FontProperties(fname="C:/Windows/Fonts/msyh.ttc")
plt.rcParams["svg.fonttype"] = "path"
fig, ax = plt.subplots(figsize=(11, 19.8), dpi=160)
fig.patch.set_facecolor("#f5f7fb")
ax.set_position((0, 0, 1, 1)); ax.set_xlim(0, 1100); ax.set_ylim(1980, 0); ax.axis("off")

def text(x, y, label, size=17, color="#172b4d", weight="normal", align="center"):
    ax.text(x, y, label, ha=align, va="center", fontsize=size, fontproperties=font, color=color, weight=weight, linespacing=1.55)

def box(x, y, w, h, title, body, fill="#ffffff", border="#c2ccdc", title_size=19):
    ax.add_patch(FancyBboxPatch((x,y),w,h,boxstyle="round,pad=0,rounding_size=16",facecolor=fill,edgecolor=border,linewidth=1.5))
    text(x+w/2,y+32,title,title_size,weight="bold")
    text(x+w/2,y+h/2+20,body,16)

def arrow(points, color="#607797", dashed=False):
    for a,b in zip(points[:-2],points[1:-1]):
        ax.plot([a[0],b[0]],[a[1],b[1]],color=color,linewidth=1.8,linestyle="--" if dashed else "-")
    ax.add_patch(FancyArrowPatch(points[-2],points[-1],arrowstyle="-|>",mutation_scale=16,color=color,linewidth=1.8,linestyle="--" if dashed else "-"))

text(550,55,"Goa2V1 · 当前模型架构",28,weight="bold")
text(550,105,"实际实现：观察 v3 / 编码器 v2 · 输入完整性尚未通过",17,"#ad4e00")
box(70,155,960,100,"C# 权威规则：GameSession","卡牌规则、结算、实际决策者、合法候选与环境投币",fill="#e8f2ff",border="#74a1d5")
arrow([(550,255),(550,290)])
box(70,290,960,110,"本人许可投影 → AI 白名单 Observation / Candidate","他人装备与牌区公开；未揭示暗选、随机数与调试状态不入网")
arrow([(550,400),(550,435)])
box(70,435,960,135,"当前缺口：投影省略 + 编码省略","有效加成 / 效果范围等尚未投影\nEffects、Purple、PublicHistory 已到观察，但网络未读取",fill="#fff0dd",border="#df9b48")
arrow([(330,570),(330,610)]); arrow([(800,570),(800,610)])
box(70,610,465,120,"局面编码 s：1805 维","牌区、阶段、攻击数值、资源\n单位位置部分聚合，存在关联损失",fill="#e8f2ff",border="#74a1d5")
box(565,610,465,120,"候选编码 a_i：242 维","稳定ID、动作种类、目标、落点\n完整合法候选集，数量 N 可变",fill="#e8f2ff",border="#74a1d5")
arrow([(300,730),(300,765),(410,765),(410,805)])
arrow([(800,730),(800,765),(410,765),(410,805)])
box(70,805,675,145,"Actor：共享候选打分网络","拼接 [s, a_i]：2047 → Linear 64 → Tanh → Linear 1\n每个候选输出 z_i；131,137 个参数",fill="#e4f5ee",border="#65a991",title_size=20)
arrow([(80,670),(40,670),(40,785),(1055,785),(1055,875),(1030,875)],dashed=True)
box(790,805,240,145,"Critic：价值网络","仅输入 s\n1805 → 64 → 1\n隐藏层 Tanh",fill="#f0edf8",border="#a594be",title_size=17)
arrow([(910,950),(910,990)],dashed=True)
box(790,990,240,170,"115,649 个参数","仅供 PPO 学习回报\n当前监督分支未训练\n不参与动作排序",fill="#f0edf8",border="#a594be",title_size=17)
arrow([(410,950),(410,990)])
box(70,990,675,100,"Softmax：合法候选之间归一化","p_i = exp(z_i) / Σ exp(z_j)",fill="#e4f5ee",border="#65a991")
arrow([(410,1090),(410,1130)])
box(70,1130,675,100,"选择稳定动作 ID","正式评测取最大概率；PPO 采样探索")
arrow([(410,1230),(410,1270)])
box(70,1270,960,115,"C# 正式 Command 校验 → 执行 → 下一决策","规则仍有最终决定权；不支持/异常保存现场并报错",fill="#e8f2ff",border="#74a1d5")
arrow([(550,1385),(550,1435)])
text(550,1450,"循环返回本人许可观察",17,"#46658b")
box(70,1520,960,275,"训练路线：与规则执行分离","当前：教师模仿 + 防御对比排序 → 只更新 Actor\n已跑通过的 PPO：团队终局奖励 + GAE → Actor 与 Critic\n当前检查点谱系没有继承 PPO 分支\n无 RNN / Transformer / 树搜索；总参数 246,786",fill="#ffffff",border="#a8b7ce")
text(550,1845,"图中橙色为已证实缺口，不代表已经修复。",18,"#ad4e00")
text(550,1890,"合法动作约束 ≠ 模型理解持续效果；需要升级输入后重新训练与评测。",15,"#52637c")
text(550,1935,"2026-10-07 · 基于代码与12项编码探针 · PNG / SVG 可放大查看",13,"#75859b")
for ext in ("png", "svg"):
    fig.savefig(args.output / ("current-model." + ext),dpi=160,facecolor=fig.get_facecolor())
print(args.output / "current-model.png")
