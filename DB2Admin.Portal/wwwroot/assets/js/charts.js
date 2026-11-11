window.SQLAZORCharts = {
    _registry: new Map(),
    _palette: ['#00F0FF', '#FF2FD0', '#7C5CFF', '#38E8A0', '#FFB020'],

    render: function (canvasId, spec) {
        if (typeof Chart === 'undefined') {
            return false;
        }

        var canvas = document.getElementById(canvasId);
        if (!canvas) {
            return false;
        }

        this.destroy(canvasId);

        var palette = this._palette;
        var type = (spec && spec.type) || 'bar';
        if (['bar', 'line', 'pie'].indexOf(type) < 0) {
            type = 'bar';
        }

        var labels = (spec && spec.labels) || [];
        var series = (spec && spec.series) || [];
        var isPie = type === 'pie';
        var rtl = !!(spec && spec.rtl);

        var datasets = series.map(function (entry, index) {
            var color = palette[index % palette.length];
            var dataset = {
                label: entry.name || ('Series ' + (index + 1)),
                data: entry.values || []
            };

            if (isPie) {
                dataset.backgroundColor = labels.map(function (_, i) { return palette[i % palette.length]; });
                dataset.borderColor = 'rgba(10, 14, 22, 0.55)';
                dataset.borderWidth = 1;
            } else if (type === 'line') {
                dataset.borderColor = color;
                dataset.backgroundColor = window.SQLAZORCharts._alpha(color, 0.16);
                dataset.fill = true;
                dataset.tension = 0.3;
                dataset.pointRadius = 2;
                dataset.pointHoverRadius = 4;
                dataset.borderWidth = 2;
            } else {
                dataset.backgroundColor = window.SQLAZORCharts._alpha(color, 0.68);
                dataset.borderColor = color;
                dataset.borderWidth = 1;
                dataset.borderRadius = 4;
            }

            return dataset;
        });

        var config = {
            type: isPie ? 'doughnut' : type,
            data: {
                labels: labels,
                datasets: isPie ? datasets.slice(0, 1) : datasets
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: { duration: 350 },
                color: '#C7D0E0',
                plugins: {
                    legend: {
                        display: true,
                        rtl: rtl,
                        labels: { color: '#C7D0E0', boxWidth: 12, boxHeight: 12, usePointStyle: true }
                    },
                    tooltip: {
                        backgroundColor: 'rgba(10, 14, 22, 0.94)',
                        borderColor: 'rgba(0, 240, 255, 0.35)',
                        borderWidth: 1,
                        titleColor: '#E6EDF3',
                        bodyColor: '#C7D0E0',
                        rtl: rtl,
                        textDirection: rtl ? 'rtl' : 'ltr'
                    }
                },
                scales: isPie ? {} : {
                    x: {
                        ticks: { color: '#93A4BF', maxRotation: 45, autoSkip: true, maxTicksLimit: 14 },
                        grid: { color: 'rgba(148, 163, 184, 0.10)' }
                    },
                    y: {
                        beginAtZero: true,
                        ticks: { color: '#93A4BF' },
                        grid: { color: 'rgba(148, 163, 184, 0.10)' }
                    }
                }
            }
        };

        var chart = new Chart(canvas.getContext('2d'), config);
        this._registry.set(canvasId, chart);
        return true;
    },

    destroy: function (canvasId) {
        var chart = this._registry.get(canvasId);
        if (chart) {
            chart.destroy();
            this._registry.delete(canvasId);
        }
    },

    destroyAll: function () {
        var charts = this._registry;
        charts.forEach(function (chart) { chart.destroy(); });
        charts.clear();
    },

    _alpha: function (hex, alpha) {
        var value = hex.replace('#', '');
        if (value.length === 3) {
            value = value[0] + value[0] + value[1] + value[1] + value[2] + value[2];
        }

        var r = parseInt(value.substring(0, 2), 16);
        var g = parseInt(value.substring(2, 4), 16);
        var b = parseInt(value.substring(4, 6), 16);
        return 'rgba(' + r + ', ' + g + ', ' + b + ', ' + alpha + ')';
    }
};
