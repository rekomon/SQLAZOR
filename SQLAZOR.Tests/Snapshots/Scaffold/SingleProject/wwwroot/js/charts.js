// Minimal ApexCharts wrapper called via IJSRuntime from Dashboard.razor. Used for the Plain and
// Tabler page styles only - MudBlazor mode renders charts with its own native <MudChart /> instead.
window.charts = {
    renderBar: function (elementId, labels, values) {
        renderApexChart(elementId, 'bar', labels, values);
    },
    renderPie: function (elementId, labels, values) {
        renderApexChart(elementId, 'pie', labels, values);
    },
    renderLine: function (elementId, labels, values) {
        renderApexChart(elementId, 'line', labels, values);
    }
};

function renderApexChart(elementId, type, labels, values) {
    var el = document.querySelector('#' + elementId);
    if (!el || typeof ApexCharts === 'undefined') return;

    var isPie = type === 'pie';
    var options = {
        chart: { type: type, height: '100%', toolbar: { show: false } },
        series: isPie ? values : [{ name: 'Value', data: values }],
        labels: isPie ? labels : undefined,
        xaxis: isPie ? undefined : { categories: labels }
    };

    new ApexCharts(el, options).render();
}