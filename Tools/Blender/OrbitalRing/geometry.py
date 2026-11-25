# SPDX-License-Identifier: Apache-2.0
# Executed as part of the isolated Blender build script.
def armor(theta, outline, tile=0, side=-1, base=.49, depth=.042, roof=False):
    # Independent long, cut-out sheet-metal plates on a sloped cross-section.
    cx=sum(p[0] for p in outline)/len(outline)
    cz=sum(p[1] for p in outline)/len(outline)
    lo=min(p[0] for p in outline);hi=max(p[0] for p in outline)
    bottom=min(p[1] for p in outline);top=max(p[1] for p in outline)
    ids=[]
    for s,z in outline:
        p=point(theta+s/R,base+depth/2,z) if roof else point(theta+s/R,z,side*(base-.55*z)+side*depth/2)
        ids.append(m.vertex(p))
    uv=[((s-lo)/(hi-lo),(z-bottom)/(top-bottom)) for s,z in outline]
    if not roof and side<0:
        ids=ids[::-1];uv=[(1-u,v) for u,v in uv][::-1]
    m.face(ids,tile,uv)
    m.parts+=1


def sheet_bolt(theta,r,side,base=.49):
    y=side*(base-.55*r)
    m.tube(point(theta,r,y+side*.025),point(theta,r,y+side*.037),.025,5,8)
    m.tube(point(theta,r,y+side*.037),point(theta,r,y+side*.052),.017,3,6)
    m.panel(theta,.019,.004,y+side*.054,8,r,depth=.004,clip=.0005)


def cheek_shape(length,style):
    profiles=[
        [(-.50,.28),(-.43,.39),(.44,.36),(.50,.26),(.38,.12),(.13,.04),(-.04,-.30),(-.25,-.27),(-.31,-.08),(-.50,-.05)],
        [(-.50,.32),(-.36,.40),(.48,.35),(.50,.06),(.28,-.07),(-.04,-.10),(-.26,-.32),(-.48,-.27)],
        [(-.49,.30),(-.40,.38),(.50,.36),(.49,-.07),(.23,-.11),(.05,-.31),(-.12,-.27),(-.26,.01),(-.50,.06)],
        [(-.50,.19),(-.30,.37),(.47,.39),(.50,.12),(.18,-.10),(-.13,-.17),(-.40,-.32),(-.50,-.26)],
    ]
    return [(s*length,r) for s,r in profiles[style%4]]


# Open rectangular-section frame. Armor will slope from a dark roof down to
# irregular white cheeks, leaving the lower utility gallery exposed.
for rr in (-.49,.48):
    for yy in (-.55,.55):
        m.rail(R+rr,yy,.040,4,count=648,sides=10)
for rr,yy,tile,radius in [(-.39,-.43,4,.027),(-.39,.43,4,.027),(-.31,-.46,6,.010),(-.31,.46,6,.010),(-.19,-.23,5,.028),(-.16,.23,5,.024),(.28,-.39,3,.031),(.28,.39,3,.031),(.49,-.28,5,.022),(.49,.28,5,.022)]:
    m.rail(R+rr,yy,radius,tile,count=648,sides=8)
for side in (-1,1):
    # Parallel exposed tubes along the inner edge, not solid inner casing.
    for rr,yy,thick,tile in [(-.53,.45,.025,3),(-.48,.33,.022,4),(-.34,.44,.006,6),(-.43,.44,.006,6),(.51,.56,.021,3)]:
        m.rail(R+rr,side*yy,thick,tile,count=648,sides=8)

front_gaps=set(range(5,9))|set(range(25,30))|set(range(44,49))|set(range(63,67))
back_gaps=set(range(6,10))|set(range(27,31))|set(range(43,48))|set(range(64,68))
for i in range(N):
    t=i*STEP
    center=t+STEP/2
    # Fine alternating diagonals: no oversized X on every face of the cage.
    for side in (-1,1):
        yy=side*.235
        m.tube(point(t,-.47,yy),point(t,.45,yy),.026,4,8)
        r0,r1=(-.43,.43) if (i+(side>0))%2 else (.43,-.43)
        m.tube(point(t+.009,r0,yy),point(t+STEP-.009,r1,yy),.018,3,8)
        # Slotted mounting shoes around the structural joints.
        for rr in (-.46,.44):
            m.panel(t,.115,.13,side*.542,3,r=rr,depth=.055,clip=.018)
        for yy0 in (-.55,.55):
            m.rail(R+.48,yy0,.057,3,start=t-.004,end=t+.004,count=2,sides=8)
            m.rail(R-.49,yy0,.055,3,start=t-.004,end=t+.004,count=2,sides=8)
    for rr in (-.47,.45):
        brace_r=.31 if rr>0 else rr
        brace_y=.23 if rr>0 else .52
        m.tube(point(t,brace_r,-brace_y),point(t,brace_r,brace_y),.024,4,8)
        if i%2==0:
            m.tube(point(t+.011,brace_r,-brace_y*.94),point(t+STEP-.011,brace_r,brace_y*.94),.016,4,8)
    # Dark recessed machinery, radiator fins, wire harnesses, and clamp straps.
    for side in (-1,1):
        m.panel(center,.78*BAY,.24,side*.35,8,r=-.23,depth=.10,clip=.025)
        for j in range(7):
            ft=center+(j-3)*.085/R
            m.panel(ft,.018,.20,side*.414,5 if j%3 else 4,r=-.23,depth=.036,clip=.003)
        # Thin yellow guard rails and their vertical stanchions.
        for shift in (.18,.70):
            ft=t+STEP*shift
            m.tube(point(ft,-.43,side*.44),point(ft,-.34,side*.44),.006,6,6)
        for rr in (-.31,-.43):
            m.rail(R+rr,side*.45,.010,7,start=center-.007,end=center+.007,count=2,sides=6)
        if i%3==1:
            m.panel(center-.18/R,.16,.15,side*.48,5,r=-.08,depth=.085,clip=.02)
            # An axial actuator casing, polished piston and end lugs.
            m.tube(point(center-.23/R,.10,side*.49),point(center+.15/R,.10,side*.49),.042,4,10)
            m.tube(point(center+.15/R,.10,side*.49),point(center+.32/R,.10,side*.49),.018,3,8)
            for ds in (-.24,.31):
                m.panel(center+ds/R,.07,.13,side*.49,3,r=.10,depth=.11,clip=.014)
        if i%3==0:
            # Small black recess with a thin cyan strip: no luminous white frame.
            ly=side*.671
            m.panel(center+.08/R,.40,.078,ly,8,r=-.26,depth=.040,clip=.012)
            m.panel(center+.08/R,.33,.052,ly+side*.024,11,r=-.26,depth=.012,clip=.003)
            m.tube(point(center-.18/R,-.28,side*.57),point(center-.18/R,-.28,side*.67),.013,5,6)
        if i%5==2:
            # Bent hydraulic hose with ferrules, routed along the rim only.
            path=[point(center-.34/R,.12,side*.37),point(center-.28/R,-.04,side*.45),point(center+.16/R,-.09,side*.45),point(center+.26/R,-.22,side*.39)]
            for a,b in zip(path,path[1:]):m.tube(a,b,.017,8,8)
            for p in (path[0],path[-1]):m.tube(p-Vector((0,.026,0)),p+Vector((0,.026,0)),.026,4,8)
    # Small raised outer docking lugs, clamps, and staggered pipe sections.
    if i%4==0:
        m.band(t,.11,.49,0,1.21,.066,3,segments=2)
        for side in (-1,1):
            m.panel(t,.15,.19,side*.60,3,r=.40,depth=.08,clip=.026)
            m.tube(point(t-.008,.51,side*.56),point(t+.008,.51,side*.56),.045,3,10)
    if i%6==0:
        for side in (-1,1):
            m.rail(R+.56,side*.40,.047,4,start=t+.012,end=t+.060,count=6,sides=10)
            m.rail(R+.56,side*.40,.020,3,start=t+.060,end=t+.078,count=3,sides=8)
            for dt in (.012,.060):
                m.band(t+dt,.075,.53,side*.40,.13,.12,3,segments=2)

# Main side armor: several independent lengths per cluster, staggered by face.
# These are large slanted plates with cut-outs and angular lower edges, not tiles.
for side,gaps in ((-1,front_gaps),(1,back_gaps)):
    i=0
    serial=0
    while i<N:
        if i in gaps:
            i+=1;continue
        span=random.choice((1.6,1.9,2.2,2.5))
        available=0
        while i+available<N and i+available not in gaps and available<3:available+=1
        span=min(span,available-.08)
        if span<.7:i+=1;continue
        length=span*BAY
        center=(i+span/2)*STEP
        style=(serial+(side>0))%4
        tile=random.choice((0,0,0,0,1,1,2,3))
        outline=cheek_shape(length,style)
        base=.493+random.uniform(-.008,.014)
        armor(center,outline,tile,side,base,depth=.043)
        cx=sum(p[0] for p in outline)/len(outline);cr=sum(p[1] for p in outline)/len(outline)
        for s,r in outline[::2]:
            ss=cx+(s-cx)*.88;rr=cr+(r-cr)*.84
            sheet_bolt(center+ss/R,rr,side,base)
        # Small irregular patch over a seam, with exposed dark overlap underneath.
        if serial%2==0:
            patch=[(-.32,.24),(-.20,.33),(.28,.30),(.31,.12),(.09,.04),(-.28,.08)]
            armor(center-.30/R,patch,3 if tile==0 else 0,side,base+.041,depth=.028)
            for ds in (-.19,.19):sheet_bolt(center+(-.30+ds)/R,.20,side,base+.04)
        if serial%4==1:
            # A narrow dark secondary skirt sits above the utility cavity.
            edge=[(-length*.38,-.14),(.20,-.15),(.35,-.25),(-length*.32,-.25)]
            armor(center,edge,2,side,base+.025,depth=.021)
        serial+=1
        i+=max(1,round(span))

# Dorsal shell in charcoal, individually staggered from the cheek plates.
i=0
while i<N:
    if i in (front_gaps & back_gaps):
        i+=1;continue
    span=random.choice((1.5,1.8,2.1))
    length=span*BAY
    t=(i+span*.5)*STEP
    outline=[(-length*.50,-.25),(-length*.43,-.36),(length*.43,-.34),(length*.50,-.23),(length*.48,.26),(length*.40,.34),(-length*.45,.34),(-length*.50,.22)]
    armor(t,outline,2,base=.413,depth=.052,roof=True)
    # Low hatch ridges, microscopic fasteners and cooling grilles on the spine.
    for ds in (-length*.37,length*.37):
        for yy in (-.25,.25):
            p=point(t+ds/R,.449,yy)
            rr=Vector((math.cos(t+ds/R),0,math.sin(t+ds/R)))
            m.tube(p,p+rr*.018,.020,3,6)
    if i%5==0:
        m.band(t,.38,.452,.04,.30,.022,15,segments=3)
    if i%7==2:
        m.band(t+.019,.23,.468,-.04,.42,.045,5,segments=2)
    i+=max(1,round(span))

