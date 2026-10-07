"""Mobile-sized technical diagram of the implemented entity policy."""
import argparse,json
from pathlib import Path
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import FancyBboxPatch,FancyArrowPatch
from matplotlib.font_manager import FontProperties

p=argparse.ArgumentParser();p.add_argument('--report',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
r=json.loads(a.report.read_text(encoding='utf-8'));assert r['complete_information'] is False
a.output.mkdir(parents=True,exist_ok=True)
font=FontProperties(fname='C:/Windows/Fonts/msyh.ttc');plt.rcParams['svg.fonttype']='path'
fig,ax=plt.subplots(figsize=(10,19),dpi=180);fig.patch.set_facecolor('#f4f7fc');ax.set_position((0,0,1,1));ax.set_xlim(0,1000);ax.set_ylim(1900,0);ax.axis('off')
def text(x,y,s,size=16,color='#172b4d'):
 ax.text(x,y,s,ha='center',va='center',fontproperties=font,fontsize=size,color=color,linespacing=1.6)
def box(x,y,w,h,title,body,fill='#ffffff',edge='#a5b8d0'):
 ax.add_patch(FancyBboxPatch((x,y),w,h,boxstyle='round,pad=0,rounding_size=14',facecolor=fill,edgecolor=edge,linewidth=1.4))
 text(x+w/2,y+30,title,20);text(x+w/2,y+h/2+23,body,15)
def arrow(x,y,x2,y2):ax.add_patch(FancyArrowPatch((x,y),(x2,y2),arrowstyle='-|>',mutation_scale=18,linewidth=1.6,color='#5478a1'))
text(500,48,'Goa2V1 · 原子信息与关系模型',27)
text(500,90,'观察 v4 / 行动 v2 / 编码器 v3 · 2026-10-08',16,'#536b8b')
box(55,130,890,105,'C#正式规则与本人许可投影','真正决策者的公开信息＋本人私有信息；暗选仍按规则遮蔽','#e7f0ff')
arrow(500,235,500,270)
box(55,270,425,165,'版本化静态资料','254格地图：坐标 / 障碍 / 出生点\n兵线 / 基地 / 区域\n108种卡：稳定ID＋数值属性','#edf3ff')
box(520,270,425,165,'当前实体与完整合法候选','每名角色 / 每个单位 / 每张装备牌\n效果 / 行动链 / 公开历史 / 时间\nC#查询的路径、攻防与升级事实','#edf3ff')
arrow(265,435,265,470);arrow(735,435,735,470)
box(55,470,890,190,'按实体存信息，按关系连接','角色 → 持有卡牌    单位 → 所在格子\n效果 → 来源 / 保护对象 / 区域 / 到期时间\n候选行动 → 使用牌 / 目标 / 目的格 / 查询结果\n实体编号只用于连边，不当作有大小含义的数字','#e8f5ef','#74ae96')
arrow(500,660,500,700)
shape=r['model_shape']
box(55,700,890,120,'节点编码 → 64维表示',f"每节点 {shape['numeric_dim']} 个数值/存在槽＋最多 {shape['category_dim']} 个类别槽\n类别Embedding 16维 → Linear＋SiLU＋LayerNorm；实体数量可变")
arrow(500,820,500,855)
box(55,855,890,125,'两层带类型的图消息传递','沿所有权、位置、效果、历史、目标等关系交换信息\n保留个体关联；不先把同队角色求和或取平均','#e8f5ef','#74ae96')
arrow(300,980,300,1020);arrow(750,980,750,1020)
box(55,1020,480,160,'Actor：合法候选逐个评分','候选节点作查询 → 4头全图注意力\n拼接自身表示与读取结果\n小型MLP → 每个候选一个logit','#e8f5ef','#74ae96')
box(585,1020,360,160,'Critic：价值估计','学习的查询 → 4头注意力\n价值头输出V\n图编码器与Actor共享','#f0ecfa','#b5a2d1')
arrow(295,1180,295,1220)
box(55,1220,480,105,'Softmax → 稳定候选ID','只在全部合法候选之间分配概率','#e8f5ef','#74ae96')
box(585,1220,360,105,'学习方式与接口分离','监督模仿 / PPO均可接入\n本批仅做短时接线验证','#f0ecfa','#b5a2d1')
arrow(295,1325,295,1360)
box(55,1360,890,105,'C#正式Execute校验 → 执行 → 下一决策','真正结束与步数截断分开；不支持或超容量即报错并保存现场','#e7f0ff')
box(55,1510,890,185,'仍未完成的语义覆盖','完整牌文机制描述、部分公开事件原因与历史算式仍待补齐\n新模型不会自动理解新卡牌；旧检查点明确拒绝新格式\n输入不完整标记保留：complete_information = false','#fff0da','#d89b4c')
text(500,1745,f"实际参数 {r['parameter_count']:,} · {r['response_windows']}种响应 / {r['response_candidates']}个候选进入网络",17)
text(500,1790,'无队伍牌区汇总 / 无单位平均位置 / 无最近敌人距离 / 无ID字节向量',14,'#526783')
text(500,1830,'两次优化器更新只验证学习接线；本批没有新模型棋力提升结论。',14,'#a15820')
text(500,1870,'图中为实际实现；PNG适合手机查看，SVG可继续放大。',13,'#657c98')
for ext in ('png','svg'):fig.savefig(a.output/('entity-model.'+ext),dpi=180,facecolor=fig.get_facecolor())
print(a.output/'entity-model.png')
