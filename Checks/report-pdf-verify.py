"""Independent PDF QA: do not confuse content-stream presence with visible content."""
from pathlib import Path
import json,re,sys
from collections import Counter
import pdfplumber
from pypdf import PdfReader

output=Path(sys.argv[1]);cases=json.loads((output/'browser-results.json').read_text(encoding='utf-8'))
def compact(s):return re.sub(r'\s+','',s)
def body_only(o):
    matrix=o.get('matrix',(1,0,0,1,0,0))
    return o.get('object_type')!='char' or abs(matrix[1])<.001 and abs(matrix[2])<.001
results=[]
for case in cases+[
    {'name':'pagination-stress','path':output/'export-regression/pagination-stress.pdf','actor':'core','markers':['RECORD-ONE','FULL-LINE-001','FULL-LINE-160','ROW-180','NOTES-180'],'absent':[]},
    {'name':'empty-officer','path':output/'export-regression/empty-officer.pdf','actor':'core','markers':['No committee reports are available'],'absent':[]},
    {'name':'empty-manager','path':output/'export-regression/empty-manager.pdf','actor':'core','markers':['No matching decisions','No results','Rejected'],'absent':[]},
    {'name':'top-long-tokens','path':output/'export-regression/top-long-tokens.pdf','actor':'core','markers':['General statistics','Tender and decision details','Supplier statistics'],'absent':[]}
]:
    path=Path(case.get('path',output/(case['name']+'.pdf')));reader=PdfReader(path);assert len(reader.pages)>0
    texts=[];drawn=[]
    with pdfplumber.open(path) as pdf:
        assert len(pdf.pages)==len(reader.pages)
        for number,page in enumerate(pdf.pages,1):
            raw=reader.pages[number-1].get_contents().get_data()
            assert raw.count(b'(CONFIDENTIAL)')==1,(case['name'],number,'Duplicate/missing watermark')
            assert ('User: pdf-qa-'+case['actor']+'@example.invalid').encode() in raw,(case['name'],number,'Account missing')
            assert b'Accessed 2026-11-14 12:34:56 UTC' in raw
            page=page.filter(body_only)
            assert all(c['x0']>=20 and c['x1']<=page.width-20 and c['top']>=20 and c['bottom']<=page.height-20 for c in page.chars if c['text'].strip()),(case['name'],number,'Clipped/out-of-bounds report text')
            visible=page.crop((0,0,page.width,page.height)).extract_text(x_tolerance=1) or ''
            assert len(visible.split())>4,(case['name'],number,'Blank report page')
            texts.append(visible);drawn.append(''.join(c['text'] for c in page.chars))
    text='\n'.join(texts);normalized=compact(text)
    # Compare the actual HTML table cells, detail values and stored recommendation
    # paragraphs against the corresponding visible PDF, independent of fixtures.
    # Arabic uses glyph/code-point preservation checks below, not a false RTL claim.
    # Original drawing order keeps a wrapped cell word together. Reading-order
    # extraction interleaves other columns; repeated headers interrupt long cells.
    drawn_key=re.sub(r'[^\w]','',''.join(drawn))
    for value in case.get('sourceValues',[]):
        if not re.search(r'[\u0600-\u06ff]',value) and value.strip():
            for token,count in Counter(re.findall(r'\w+',value)).items():
                assert drawn_key.count(token)>=count,(case['name'],'HTML value missing from visible PDF',value[:180],token)
    for marker in case['markers']:assert compact(marker) in normalized,(case['name'],'Missing visible PDF content',marker)
    for marker in case['absent']:assert compact(marker) not in normalized,(case['name'],'Wrong context data',marker)
    if 'REPORT-LINE-001' in case['markers']:
        for i in range(1,121):
            marker=f'REPORT-LINE-{i:03}'
            assert normalized.count(marker)==(2 if case['name']=='officer-list' else 1),(case['name'],'Missing/duplicated report line',i)
    if case['name']=='manager-decisions':
        for i in range(1,36):assert f'DECISION-NOTE-{i:03}' in normalized
    if case['name']=='saved-ai':
        for i in range(1,46):assert f'AI-REASON-{i:03}' in normalized
    if case['name'] in ['supplier-existing','top-unified']:
        for i in range(1,66):assert f'ITEM-{i:03}' in normalized and f'OFFER-SPEC-ITEM-{i:03}' in normalized
        assert compact('العربية محفوظة') in compact(''.join(drawn)),'Arabic Unicode code points lost; shaping remains the existing library limitation'
    if case['name']=='pagination-stress':
        for i in range(1,161):assert f'FULL-LINE-{i:03}' in normalized
        for i in range(1,181):assert f'ROW-{i:03}' in normalized and f'NOTES-{i:03}' in normalized
        # Repeated table headers and adjacent columns interleave extraction order;
        # count every glyph rather than requiring one uninterrupted token string.
        assert normalized.count('X')==1800,'Long unbroken token lost characters'
        assert 'H'*200 in drawn_key,'Long report heading was truncated'
    if case['name']=='top-long-tokens':
        for token in ['W'*200,'T'*200,'S'*200]:assert token in drawn_key,'Top Committee unbroken filter/title/supplier token was truncated'
    if case['name'] not in ['top-existing','empty-officer','empty-manager','top-long-tokens']:assert len(reader.pages)>1,(case['name'],'Long report must paginate')
    path.with_suffix('.txt').write_text(text,encoding='utf-8')
    results.append({'report':case['name'],'pages':len(reader.pages),'complete_visible_data':True,'bounds':'pass','blank_pages':False,'watermark':'exactly once per page'})
    print('PASS independent PDF checks:',case['name'],len(reader.pages),'pages')
(output/'pdf-results.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
uploaded=PdfReader(output/'supplier-uploaded-document.pdf')
assert len(uploaded.pages)==3
for page in uploaded.pages:
    raw=page.get_contents().get_data()
    assert raw.count(b'(CONFIDENTIAL)')==1 and b'pdf-qa-supplier@example.invalid' in raw and b'Accessed 2026-11-14 12:34:56 UTC' in raw
print('PASS existing stored/uploaded PDF download: three pages watermarked once; original hash unchanged in HTTP checks.')
print('PASS',len(results),'PDF documents; Arabic code points retained. Arabic shaping/RTL is NOT claimed fixed.')
