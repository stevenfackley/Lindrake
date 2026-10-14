// entityGraph.js — vis-network driver for the OmniSift Entity Graph page

export function renderGraph(containerId, nodes, edges) {
    const container = document.getElementById(containerId);
    if (!container) return;

    const visNodes = new vis.DataSet(nodes.map(n => ({
        id: n.id,
        label: n.label + '\n(' + n.type + ')',
        color: nodeColor(n.type),
        font: { color: '#ECE5D6', face: 'IBM Plex Mono', size: 11 },
        borderWidth: 1,
        borderWidthSelected: 2,
    })));

    const visEdges = new vis.DataSet(edges.map((e, i) => ({
        id: i,
        from: e.source,
        to: e.target,
        label: e.relationship,
        color: { color: 'rgba(236,229,214,0.18)', highlight: '#E8A23D' },
        font: { color: '#9A958A', face: 'IBM Plex Mono', size: 9, align: 'middle' },
        arrows: 'to',
        smooth: { type: 'curvedCW', roundness: 0.2 },
    })));

    const options = {
        nodes: { shape: 'dot', size: 16, borderColor: '#E8A23D' },
        edges: { width: 1 },
        physics: {
            stabilization: { iterations: 150 },
            barnesHut: { gravitationalConstant: -3000, springLength: 120 },
        },
        interaction: { hover: true, tooltipDelay: 200 },
        background: { color: 'transparent' },
    };

    new vis.Network(container, { nodes: visNodes, edges: visEdges }, options);
}

function nodeColor(type) {
    switch (type) {
        case 'person': return { background: '#1B1E25', border: '#E8A23D', highlight: { background: '#1B1E25', border: '#F2B65A' } };
        case 'org':    return { background: '#1B1E25', border: '#46C68A', highlight: { background: '#1B1E25', border: '#46C68A' } };
        case 'place':  return { background: '#1B1E25', border: '#60A5FA', highlight: { background: '#1B1E25', border: '#60A5FA' } };
        case 'date':   return { background: '#1B1E25', border: '#9A958A', highlight: { background: '#1B1E25', border: '#9A958A' } };
        case 'event':  return { background: '#1B1E25', border: '#E8A23D', highlight: { background: '#1B1E25', border: '#F2B65A' } };
        default:       return { background: '#1B1E25', border: '#E8A23D' };
    }
}
