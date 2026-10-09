"""Original editable card reliefs. Semantic construction, not stock glyph labels."""
import math,bpy
from mathutils import Vector
from r2b02_stones import poly,path,mesh

class Sculpt:
    def __init__(self,collection,materials):self.c=collection;self.m=materials
    def plate(self,name,points,mat='metal',z=.25,depth=.10,bevel=.025):return poly(name,points,z,z+depth,self.c,self.m[mat],bevel)
    def line(self,name,points,mat='bronze',radius=.025):return path(name,points,radius,self.c,self.m[mat])
    def arc(self,name,r,start=0,end=math.tau,center=(0,0,.3),mat='bronze',width=.035,steps=80):
        return self.line(name,[(center[0]+r*math.cos(start+(end-start)*i/steps),center[1]+r*math.sin(start+(end-start)*i/steps),center[2]) for i in range(steps+1)],mat,width)
    def ball(self,name,loc,scale,mat='metal',smooth=True):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,radius=1,location=loc)
        ob=bpy.context.object;ob.name=name
        for c in list(ob.users_collection):c.objects.unlink(ob)
        self.c.objects.link(ob);ob.scale=scale;ob.data.materials.append(self.m[mat])
        for p in ob.data.polygons:p.use_smooth=smooth
        return ob
    def crystal(self,name,loc=(0,0,.4),size=.3,mat='glow'):
        x,y,z=loc;v=[(x,y+size,z),(x+size*.65,y,z),(x,y-size,z),(x-size*.65,y,z),(x,y,z+size*.65),(x,y,z-size*.24)]
        return mesh(name,v,[(i,(i+1)%4,4) for i in range(4)]+[((i+1)%4,i,5) for i in range(4)],self.c,self.m[mat])
    def bolt(self,name,points,width=.045):
        self.line(name+' electric sheath',points,'energy',width)
        self.line(name+' bright core',[(x,y,z+.008) for x,y,z in points],'hot',width*.27)
    def blade(self,name,x=0,y=0,angle=0,length=1.55):
        parts=[]
        parts.append(self.plate(name+' dark forged seat',[(-.17,-.18),(-.17,length*.67),(0,length),(.17,length*.67),(.17,-.18)],'dark',.24,.13))
        parts.append(self.plate(name+' sharpened left bevel',[(-.13,-.1),(-.13,length*.65),(0,length*.94),(0,-.1)],'edge',.35,.065,.008))
        parts.append(self.plate(name+' right steel bevel',[(0,-.1),(0,length*.94),(.13,length*.65),(.13,-.1)],'metal',.35,.05,.008))
        parts.append(self.plate(name+' swept guard',[(-.42,-.10),(-.36,-.24),(0,-.15),(.36,-.24),(.42,-.10),(0,-.02)],'bronze',.38,.1))
        parts.append(self.plate(name+' leather grip',[(-.085,-.20),(.085,-.20),(.07,-.62),(-.07,-.62)],'dark',.28,.13,.02))
        for yy in (-.28,-.37,-.46,-.55):parts.append(self.line(name+' grip wire',[(-.08,yy,.43),(.08,yy-.045,.43)],'bronze',.011))
        parts.append(self.crystal(name+' pommel',(0,-.66,.4),.13,'glow'))
        for ob in parts:ob.rotation_euler.z=angle;ob.location.x+=x;ob.location.y+=y
    def shield(self,name,reflect=False):
        pts=[(0,1.18),(.46,.99),(.86,.92),(.76,-.32),(.43,-.72),(0,-1.08),(-.43,-.72),(-.76,-.32),(-.86,.92),(-.46,.99)]
        self.plate(name+' thick backing',pts,'dark',.15,.20,.045)
        self.plate(name+' bevel rim',[(x*.94,y*.94) for x,y in pts],'bronze',.34,.1,.025)
        self.plate(name+' porcelain left panel',[(0,1.0),(-.70,.78),(-.60,-.30),(0,-.88)],'edge',.42,.11,.02)
        self.plate(name+' right panel',[(0,1.0),(.70,.78),(.60,-.30),(0,-.88)],'metal',.42,.08,.02)
        self.line(name+' conductive spine',[(0,-.79,.58),(0,.92,.58)],'glow',.026)
        self.crystal(name+' powered boss',(0,.18,.64),.27,'glow')
        for side in (-1,1):
            self.line(name+' inlaid circuit',[(side*.49,.66,.56),(side*.34,.30,.57),(side*.40,-.1,.57),(side*.2,-.39,.56)],'bronze',.017)
        if reflect:
            self.bolt('Incoming projectile',[(-1.23,.72,.5),(-1,.40,.53),(-.9,.52,.54),(-.48,.14,.6)],.045)
            self.bolt('Reflected projectile',[(-.48,.14,.6),(-.76,.95,.64),(-.59,.82,.65),(-.57,1.34,.66)],.05)
            self.plate('Return arrowhead',[(-.69,1.23),(-.57,1.50),(-.40,1.2)],'glow',.62,.08,.008)
    def hand(self,name,center=(0,-.28),metal=True):
        x,y=center;mat='edge' if metal else 'sand'
        self.ball(name+' palm',(x,y,.38),(.40,.51,.16),mat)
        self.plate(name+' cuff',[(x-.38,y-.40),(x+.36,y-.40),(x+.44,y-.75),(x-.43,y-.75)],'bronze',.2,.21,.035)
        for i in range(4):
            xx=x+(i-1.5)*.17;top=y+.73-(abs(i-1.5)*.13)
            points=[(xx,y+.21,.4),(xx-.01,y+.44,.52),(xx+.04,top,.59),(xx+.11,top+.04,.52)]
            self.line(name+' articulated finger '+str(i),points,mat,.067)
            self.ball(name+' finger knuckle '+str(i),points[1],(.072,.073,.070),'bronze')
        self.line(name+' opposable thumb',[(x-.28,y-.08,.45),(x-.52,y+.14,.47),(x-.5,y+.32,.48)],mat,.09)
        self.crystal(name+' cuff stone',(x,y-.58,.45),.12,'glow')
    def fragment(self,i,loc,scale=.18):
        x,y,z=loc
        self.plate('Levitating mineral '+str(i),[(x-scale,y-scale*.7),(x+scale*.6,y-scale),(x+scale,y+.1*scale),(x+.2*scale,y+scale),(x-.8*scale,y+.6*scale)],'stone',z,scale*.9,.018)
        self.line('Mineral live fissure '+str(i),[(x-scale*.6,y,z+scale*.92),(x,y+.05,z+scale*.94),(x+scale*.6,y-.02,z+scale*.93)],'glow',.014)

def wasp(key,c,m):
    s=Sculpt(c,m)
    if key=='gold':
        s.blade('Shining energy blade',y=-.35,angle=-.30,length=1.65)
        for side in (-1,1):s.bolt('Blade corona '+str(side),[(side*.9,-.5,.30),(side*.63,-.14,.38),(side*.77,.18,.39),(side*.32,.64,.42),(side*.42,1.03,.43)],.029)
        s.arc('Interrupted spell seal',.68,.15,2.3,center=(.2,.05,.2),mat='bronze',width=.025)
    elif key=='red-base':
        s.hand('Shock gauntlet',(0,-.48))
        for i in range(5):
            angle=(i/4)*math.pi;s.bolt('Contact lightning '+str(i),[(.12*math.cos(angle),.45,.60),(.52*math.cos(angle),.63+.31*math.sin(angle),.55),(.45*math.cos(angle),.85+.19*math.sin(angle),.6),(.95*math.cos(angle),.78+.4*math.sin(angle),.52)],.035)
    elif key=='red-a':
        from r2b01_boomerang_hero import build_icon
        donor={'steel':m['metal'],'edge':m['edge'],'bronze':m['bronze'],'dark':m['dark'],'rock':m['stone'],'orange':m['energy'],'orange_hot':m['hot']}
        obs=build_icon(c,donor);root=obs[0];root.location.y=-.85;root.scale=(.9,)*3
    elif key=='red-b':
        s.ball('Compression core',(0,0,.34),(.36,.36,.26),'dark');s.crystal('Ionized heart',(0,0,.55),.29,'glow')
        for j,r in enumerate((.56,.83,1.12)):
            for i in range(3):s.arc('Outgoing wave segment',r,i*2.094+.12+j*.17,i*2.094+1.57+j*.17,mat='energy',width=.04-j*.005)
        for i in range(6):
            a=i*math.tau/6;s.bolt('Radial discharge',[(.25*math.cos(a),.25*math.sin(a),.53),(.65*math.cos(a+.12),.65*math.sin(a+.12),.49),(.8*math.cos(a-.1),.8*math.sin(a-.1),.46),(1.3*math.cos(a),1.3*math.sin(a),.4)],.021)
    elif key=='silver':
        pts=[(1.06*math.cos(i*math.tau/6),1.06*math.sin(i*math.tau/6),.3) for i in range(7)]
        s.line('Static lock hexagon',pts,'bronze',.07)
        for i in range(6):
            a=i*math.tau/6;s.crystal('Field electrode',(1.06*math.cos(a),1.06*math.sin(a),.39),.17,'edge')
            s.bolt('Inward field fence',[(.87*math.cos(a),.87*math.sin(a),.43),(.69*math.cos(a+.2),.69*math.sin(a+.2),.5),(.31*math.cos(a),.31*math.sin(a),.52)],.025)
        s.arc('Restrained static field',.58,mat='glow',width=.026);s.crystal('Fixed anchor',(0,0,.47),.24,'bronze')
    elif key=='green-base':s.shield('Blocking barrier')
    elif key=='green-a':s.shield('Reflecting barrier',True)
    elif key=='green-b':
        s.hand('Telekinetic hand',(0,-.55));s.fragment(0,(0,.75,.50),.32)
        for j in range(3):s.arc('Suspension tether',.40+j*.14,.1,math.pi-.1,center=(0,.52,.40),mat='energy',width=.021)
        s.bolt('Raised object link',[(-.24,.05,.59),(-.36,.32,.57),(-.2,.46,.55),(-.24,.63,.55)],.016)
    elif key=='blue-base':
        for i,(x,y,z) in enumerate(((-.66,-.15,.35),(.61,.1,.46),(.02,.77,.36))):s.fragment(i,(x,y,z),.29)
        s.arc('Telekinesis orbit',1.02,.2,5.8,mat='bronze',width=.043)
        s.arc('Telekinesis active trail',.86,3.4,5.9,mat='energy',width=.035)
        s.crystal('Focal mental spark',(0,-.3,.52),.23,'glow')
    elif key=='blue-a':
        s.crystal('Kinetic drive',(0,0,.48),.36,'glow');s.arc('Compression brass collar',.46,mat='bronze',width=.075)
        for i in range(6):
            a=i*math.tau/6
            ob=s.plate('Outward force fin '+str(i),[(-.12,.65),(0,1.33),(.12,.65),(0,.79)],'edge',.32,.14,.009);ob.rotation_euler.z=a
            s.bolt('Push wake '+str(i),[(.44*math.sin(a),.44*math.cos(a),.52),(.70*math.sin(a+.07),.70*math.cos(a+.07),.48),(1.08*math.sin(a),1.08*math.cos(a),.5)],.017)
    elif key=='blue-b':
        s.ball('Gravity well',(0,0,.16),(.56,.56,.11),'dark')
        for j in range(4):
            pts=[]
            for i in range(100):
                t=i/99;a=j*math.pi*.5+t*math.tau*1.12;r=.18+t*1.04
                pts.append((r*math.cos(a),r*math.sin(a),.33+.08*t))
            s.line('Spiral gravity ribbon '+str(j),pts,'bronze' if j%2 else 'energy',.045 if j%2 else .029)
        for i in range(4):a=i*1.5;s.fragment(i,(1.05*math.cos(a),1.05*math.sin(a),.45),.15)
    elif key=='purple':
        s.arc('Storm halo',.89,mat='bronze',width=.075)
        s.plate('Valkyrie storm crown',[(-.91,.5),(-.72,1.12),(-.33,.80),(0,1.32),(.33,.80),(.72,1.12),(.91,.5),(.60,.55),(0,.91),(-.60,.55)],'edge',.22,.16)
        for side in (-1,1):
            s.bolt('Heaven split '+str(side),[(side*.48,.94,.54),(side*.10,.28,.59),(side*.40,.36,.62),(side*.14,-.42,.64),(side*.32,-.32,.65),(0,-1.28,.57)],.085)
        s.crystal('Thunder oath',(0,.16,.72),.22,'glow')
    else:raise ValueError(key)

BUILDERS={'wasp':wasp}
