import sys, asyncio, math
from playwright.async_api import async_playwright
OUT = sys.argv[1]

# ---------- colores por elemento (convención del GDD, sección 4.1) ----------
COL = {  # luz, base, sombra, texto-sobre-esfera, letra-estructural
 'H': ('#FFFFFF','#EDEDED','#B9B9B9','#0B2545','#6B6B6B'),
 'O': ('#FF8B7A','#DA4B3D','#8F2318','#FFFFFF','#D94A3D'),
 'C': ('#7C7C7C','#4A4A4A','#262626','#FFFFFF','#0B2545'),
 'N': ('#8FB0FF','#3F6FD8','#1F3F91','#FFFFFF','#3F6FD8'),
 'Cl':('#6FD18C','#2E8B4A','#145A2B','#FFFFFF','#2E8B4A'),
 'Si':('#F3DDA5','#D2AE63','#8F7231','#0B2545','#A8812C'),
}
RAD = {'H':.32,'O':.45,'C':.46,'N':.44,'Cl':.56,'Si':.58}

class Mol:
    def __init__(s): s.a=[]; s.b=[]
    def atom(s,el,x,y): s.a.append((el,x,y)); return len(s.a)-1
    def bond(s,i,j,o=1): s.b.append((i,j,o))
    def attach(s,i,el,dx,dy,o=1):
        j=s.atom(el,s.a[i][1]+dx,s.a[i][2]+dy); s.bond(i,j,o); return j

def hcl():
    m=Mol(); h=m.atom('H',-.55,0); c=m.atom('Cl',.55,0); m.bond(h,c); return m
def h2o():
    m=Mol(); o=m.atom('O',0,.3); m.attach(o,'H',-.79,-.62); m.attach(o,'H',.79,-.62); return m
def nh3():
    m=Mol(); n=m.atom('N',0,.1); m.attach(n,'H',0,-1); m.attach(n,'H',-.87,.62); m.attach(n,'H',.87,.62); return m
def cross(el):
    m=Mol(); c=m.atom(el,0,0)
    for dx,dy in((-1,0),(1,0),(0,-1),(0,1)): m.attach(c,'H',dx,dy)
    return m
def o2():
    m=Mol(); a=m.atom('O',-.6,0); b=m.atom('O',.6,0); m.bond(a,b,2); return m
def n2():
    m=Mol(); a=m.atom('N',-.6,0); b=m.atom('N',.6,0); m.bond(a,b,3); return m
def co2():
    m=Mol(); c=m.atom('C',0,0); m.attach(c,'O',-1.2,0,2); m.attach(c,'O',1.2,0,2); return m
def etanol():
    m=Mol(); c1=m.atom('C',0,0); c2=m.attach(c1,'C',.95,-.5); o=m.attach(c2,'O',.95,.5)
    for dx,dy in((-1,0),(0,-1),(0,1)): m.attach(c1,'H',dx,dy)
    m.attach(c2,'H',0,-1); m.attach(c2,'H',0,1)   # H arriba y abajo del C2
    m.attach(o,'H',.95,-.5); return m
def glucosa():
    m=Mol(); c=[]
    for i in range(6):
        c.append(m.atom('C',i*.95,-.5 if i%2 else 0))
        if i: m.bond(c[i-1],c[i])
    def oh(i,up):
        s=-1 if up else 1
        o=m.attach(c[i],'O',0,s); m.attach(o,'H',0,s)
    def h(i,up): m.attach(c[i],'H',0,-1 if up else 1)
    # C1 aldehído: C=O arriba, H abajo
    m.attach(c[0],'O',-.5,-.87,2); m.attach(c[0],'H',-.5,.87)
    for i in (1,2,3,4):
        up = (i%2==1)           # picos: OH arriba; valles: OH abajo
        oh(i,up); h(i,not up)
    oh(5,True); h(5,False); m.attach(c[5],'H',.9,0)  # CH2OH
    return m

# nombre, fórmula plana, fórmula HTML, nivel, molécula, qué es, cómo armarla
CARDS = [
 ("01_HCl","Cloruro de hidrógeno","HCl","HCl",1,hcl,
  "Un gas formado por un átomo de hidrógeno y uno de cloro. Disuelto en agua es el ácido clorhídrico, que también está en el jugo gástrico de tu estómago.",
  "H y Cl tienen valencia 1: solo pueden hacer un enlace, así que se unen con una barra simple."),
 ("02_H2O","Agua","H2O","H<sub>2</sub>O",1,h2o,
  "Dos hidrógenos unidos a un oxígeno. Su forma doblada (unos 104°) hace que el agua sea líquida a temperatura ambiente y disuelva muchísimas sustancias.",
  "El oxígeno tiene valencia 2: cada barra simple lo conecta con un hidrógeno."),
 ("03_NH3","Amoníaco","NH3","NH<sub>3</sub>",1,nh3,
  "Un gas de olor muy fuerte. Se usa en productos de limpieza y para fabricar fertilizantes que ayudan a crecer a las plantas.",
  "El nitrógeno tiene valencia 3: une tres hidrógenos, uno por cada barra simple."),
 ("04_CH4","Metano","CH4","CH<sub>4</sub>",1,lambda:cross('C'),
  "El componente principal del gas natural, el que alimenta las estufas. Es la molécula orgánica más simple que existe.",
  "El carbono tiene valencia 4: se conecta con cuatro hidrógenos usando cuatro barras simples."),
 ("05_O2","Oxígeno molecular","O2","O<sub>2</sub>",2,o2,
  "El oxígeno que respiras. Dos átomos de oxígeno comparten dos pares de electrones, por eso están unidos con un enlace doble.",
  "Cada oxígeno tiene valencia 2: se unen con una sola barra doble."),
 ("06_CO2","Dióxido de carbono","CO2","CO<sub>2</sub>",2,co2,
  "El gas que exhalas al respirar y el que forma las burbujas de las gaseosas. Las plantas lo usan para hacer su alimento.",
  "El carbono (valencia 4) se une a cada oxígeno (valencia 2) con una barra doble."),
 ("07_N2","Nitrógeno molecular","N2","N<sub>2</sub>",2,n2,
  "Forma casi el 78 % del aire. Sus dos átomos están unidos por un enlace triple, uno de los más fuertes que existen, por eso es tan poco reactivo.",
  "Cada nitrógeno tiene valencia 3: se unen con una sola barra triple."),
 ("08_SiH4","Silano","SiH4","SiH<sub>4</sub>",2,lambda:cross('Si'),
  "El silicio es “primo” del carbono y también forma cuatro enlaces. Este gas se usa para fabricar chips de computador y paneles solares.",
  "El silicio tiene valencia 4, igual que el carbono: cuatro barras simples a cuatro hidrógenos."),
 ("09_C2H6O","Etanol","C2H6O","C<sub>2</sub>H<sub>6</sub>O",3,etanol,
  "El alcohol de los desinfectantes y de algunos combustibles. Tiene los mismos átomos que el dimetil éter, pero ordenados distinto: ¡la estructura cambia la sustancia!",
  "Une los 2 carbonos: uno con 3 H, el otro con 2 H y un grupo O–H."),
 ("10_C6H12O6","Glucosa (cadena abierta)","C6H12O6","C<sub>6</sub>H<sub>12</sub>O<sub>6</sub>",3,glucosa,
  "El azúcar que le da energía a tus células. En su forma de cadena abierta hay 6 carbonos en fila, con grupos O–H y un enlace doble C=O en un extremo.",
  "Cadena de 6 C. El primero lleva C=O (barra doble); los demás, grupos O–H y H."),
]
LEVELS = {1:("APRENDIZ","#8FE3F7","#0B2545"),2:("TÉCNICO","#5AA9F2","#0B2545"),3:("MAESTRO","#1B4FBF","#FFFFFF")}

# ---------- dibujo ----------
def rf(m): return .76 if len(m.a)>10 else 1
def fit(m,w,h,pad,maxs):
    xs=[];ys=[]
    for el,x,y in m.a:
        r=RAD[el]*rf(m); xs+= [x-r,x+r]; ys+=[y-r,y+r]
    bw=max(xs)-min(xs); bh=max(ys)-min(ys)
    s=min(maxs,(w-2*pad)/bw,(h-2*pad)/bh)
    cx=(max(xs)+min(xs))/2; cy=(max(ys)+min(ys))/2
    return s,lambda x,y:(w/2+(x-cx)*s, h/2+(y-cy)*s)

def offsets(n,d):  # desplazamientos perpendiculares
    return {1:[0],2:[-d/2,d/2],3:[-d,0,d]}[n]

def svg(m,mode,w,h):
    s,P=fit(m,w,h,28,150)
    out=[f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}"><defs>']
    for el,(l,b,d,_,_) in COL.items():
        out.append(f'<radialGradient id="g{el}" cx="35%" cy="30%" r="80%"><stop offset="0" stop-color="{l}"/><stop offset=".5" stop-color="{b}"/><stop offset="1" stop-color="{d}"/></radialGradient>')
    out.append('</defs>')
    fs=max(20,min(58,.62*s))
    for i,j,o in m.b:
        (e1,x1,y1),(e2,x2,y2)=m.a[i],m.a[j]
        ax,ay=P(x1,y1); bx,by=P(x2,y2)
        L=math.hypot(bx-ax,by-ay); ux,uy=(bx-ax)/L,(by-ay)/L; nx,ny=-uy,ux
        if mode=='struct':
            g1=fs*.62; g2=fs*.62
            sx,sy=ax+ux*g1,ay+uy*g1; ex,ey=bx-ux*g2,by-uy*g2
            d=max(8,.13*s)
            for off in offsets(o,d):
                out.append(f'<line x1="{sx+nx*off:.1f}" y1="{sy+ny*off:.1f}" x2="{ex+nx*off:.1f}" y2="{ey+ny*off:.1f}" stroke="#0B2545" stroke-width="{max(3.5,.055*s):.1f}" stroke-linecap="round"/>')
        else:
            wd=.17*s if o==1 else .11*s
            d=.15*s
            for off in offsets(o,d):
                out.append(f'<line x1="{ax+nx*off:.1f}" y1="{ay+ny*off:.1f}" x2="{bx+nx*off:.1f}" y2="{by+ny*off:.1f}" stroke="#3E5C7E" stroke-width="{wd+3:.1f}" stroke-linecap="round"/>')
                out.append(f'<line x1="{ax+nx*off:.1f}" y1="{ay+ny*off:.1f}" x2="{bx+nx*off:.1f}" y2="{by+ny*off:.1f}" stroke="#C9D8EA" stroke-width="{wd:.1f}" stroke-linecap="round"/>')
    for el,x,y in m.a:
        px,py=P(x,y)
        if mode=='struct':
            f=fs*(.82 if el=='H' else 1)
            out.append(f'<text x="{px:.1f}" y="{py:.1f}" text-anchor="middle" dominant-baseline="central" font-family="Poppins" font-weight="700" font-size="{f:.0f}" fill="{COL[el][4]}">{el}</text>')
        else:
            r=RAD[el]*s*rf(m)
            out.append(f'<circle cx="{px:.1f}" cy="{py+r*.08:.1f}" r="{r:.1f}" fill="#000" opacity=".12"/>')
            out.append(f'<circle cx="{px:.1f}" cy="{py:.1f}" r="{r:.1f}" fill="url(#g{el})" stroke="{COL[el][2]}" stroke-width="2"/>')
            out.append(f'<text x="{px:.1f}" y="{py+1:.1f}" text-anchor="middle" dominant-baseline="central" font-family="Poppins" font-weight="700" font-size="{max(15,r*.95):.0f}" fill="{COL[el][3]}">{el}</text>')
    out.append('</svg>'); return ''.join(out)

def pieces(m):
    at={}; 
    for el,_,_ in m.a: at[el]=at.get(el,0)+1
    br={1:0,2:0,3:0}
    for _,_,o in m.b: br[o]+=1
    return at,br

def atom_chip(el,n):
    l,b,d,t,_=COL[el]
    return (f'<div class="chip"><span class="dot" style="background:radial-gradient(circle at 35% 30%,{l},{b} 55%,{d});color:{t};border-color:{d}">{el}</span>'
            f'<span class="x">× {n}</span></div>')
def bar_chip(o,n,name):
    lines=''.join(f'<rect x="4" y="{y}" width="62" height="7" rx="3.5" fill="#C9D8EA" stroke="#3E5C7E" stroke-width="2"/>' for y in {1:[17],2:[10,24],3:[3,17,31]}[o])
    return (f'<div class="chip"><svg width="56" height="34" viewBox="0 0 70 42">{lines}</svg>'
            f'<span class="x">{name} × {n}</span></div>')

HTML = """<!doctype html><html><head><meta charset="utf-8"><style>
*{box-sizing:border-box;margin:0}
html,body{background:transparent}
body{width:1280px;height:800px;font-family:'Poppins',sans-serif;color:#0B2545}
.card{position:absolute;inset:0;border-radius:56px;background:#EEF6FF;border:14px solid #17407A;box-shadow:inset 0 0 0 6px #BBD6F2;overflow:hidden}
.top{height:92px;background:#2A7DE1;border-bottom:8px solid #17407A;display:flex;align-items:center;justify-content:space-between;padding:0 44px}
.top .t{font-weight:700;font-size:38px;letter-spacing:5px;color:#FFFFFF}
.badge{background:@LC@;font-weight:700;font-size:28px;padding:6px 26px;border-radius:40px;border:5px solid #FFFFFF;color:@LT@;letter-spacing:2px}
.main{display:flex;height:496px;padding:20px 28px 0 36px;gap:22px}
.left{width:372px;display:flex;flex-direction:column}
.formula{font-weight:700;font-size:@FS@px;line-height:1.05;white-space:nowrap}
.formula sub{font-size:.55em;vertical-align:baseline;position:relative;top:.18em}
.name{font-weight:600;font-size:32px;color:#1F5FAE;margin:2px 0 14px}
.txt{font-size:22px;line-height:1.42;color:#24466E;font-weight:500}
.panel{width:402px;background:#FFFFFF;border:5px solid #A9CCF0;border-radius:30px;display:flex;flex-direction:column;align-items:center;overflow:hidden}
.panel h3{width:100%;text-align:center;background:#DCEBFC;font-size:19px;letter-spacing:2px;font-weight:700;color:#1F5FAE;padding:9px 0;border-bottom:4px dashed #A9CCF0}
.panel .img{flex:1;display:flex;align-items:center;justify-content:center}
.bot{position:absolute;left:0;right:0;bottom:0;height:176px;background:#DCEBFC;border-top:6px dashed #7FB0E6;padding:12px 36px;display:flex;gap:30px}
.bl{width:800px}
.lbl{font-weight:700;font-size:19px;letter-spacing:2px;color:#1F5FAE;margin-bottom:8px}
.chips{display:flex;flex-wrap:wrap;gap:10px 12px}
.chip{display:flex;align-items:center;gap:6px;background:#FFFFFF;border:4px solid #A9CCF0;border-radius:40px;padding:3px 14px 3px 5px}
.dot{width:40px;height:40px;border-radius:50%;border:3px solid;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:19px}
.x{font-weight:700;font-size:20px;white-space:nowrap}
.br{flex:1;min-width:0;background:#FFFFFF;border:4px solid #A9CCF0;border-radius:26px;padding:12px 20px;display:flex;gap:14px;align-items:center}
.bulb{font-size:44px}
.tip{font-size:18px;line-height:1.35;font-weight:600;color:#24466E}
.tip b{display:block;font-size:17px;letter-spacing:2px;color:#1F5FAE}
</style></head><body><div class="card">
 <div class="top"><span class="t">NUEVO PEDIDO</span><span class="badge">NIVEL @LVL@ · @LN@</span></div>
 <div class="main">
  <div class="left"><div class="formula">@F@</div><div class="name">@N@</div><div class="txt">@TXT@</div></div>
  <div class="panel"><h3>FÓRMULA ESTRUCTURAL</h3><div class="img">@S1@</div></div>
  <div class="panel"><h3>ESFERAS Y BARRAS</h3><div class="img">@S2@</div></div>
 </div>
 <div class="bot">
  <div class="bl"><div class="lbl">PARA ARMARLA NECESITAS</div><div class="chips">@CHIPS@</div></div>
  <div class="br"><div class="bulb">💡</div><div class="tip"><b>CÓMO SE UNE</b>@TIP@</div></div>
 </div>
</div></body></html>"""

async def main():
    async with async_playwright() as p:
        b=await p.chromium.launch()
        pg=await b.new_page(viewport={"width":1280,"height":800},device_scale_factor=2)
        for fn,name,plain,fhtml,lvl,mk,txt,tip in CARDS:
            m=mk(); at,br=pieces(m)
            chips=''.join(atom_chip(e,n) for e,n in at.items())
            chips+=''.join(bar_chip(o,n,nm) for o,nm in((1,'simple'),(2,'doble'),(3,'triple')) if (n:=br[o]))
            ln,lc,lt=LEVELS[lvl]
            fs=140 if len(plain)<=4 else 112 if len(plain)<=6 else 84
            h=(HTML.replace('@LC@',lc).replace('@LT@',lt).replace('@LVL@',str(lvl)).replace('@LN@',ln).replace('@FS@',str(fs))
               .replace('@F@',fhtml).replace('@N@',name).replace('@TXT@',txt).replace('@TIP@',tip)
               .replace('@CHIPS@',chips).replace('@S1@',svg(m,'struct',392,424)).replace('@S2@',svg(m,'ball',392,424)))
            await pg.set_content(h); await pg.wait_for_timeout(200)
            await pg.screenshot(path=f"{OUT}/Pedido_{fn}.png",omit_background=True)
            print(fn,at,br)
        await b.close()
asyncio.run(main())
