// APL.Net - Rust/Rayon reference benchmarks (out-of-band, no FFI).
// Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.
//
// Mirrors B1 (trivial arithmetic), B4 (AXPY over floats) and B5 (sum of doubles) at the same sizes
// and with the same data shapes as benchmarks/APL.Net.Benchmarks. Run with:
//     cargo bench --manifest-path benchmarks/rust-rayon/Cargo.toml
use criterion::{black_box, criterion_group, criterion_main, BenchmarkId, Criterion};
use rayon::prelude::*;

fn trivial(c: &mut Criterion) {
    let mut group = c.benchmark_group("B1_trivial_x2p1");
    for &n in &[10_000usize, 1_000_000, 100_000_000] {
        let src: Vec<f64> = (0..n).map(|i| (i as f64) * 0.25).collect();
        let mut dst = vec![0.0f64; n];
        group.bench_with_input(BenchmarkId::new("sequential", n), &n, |b, _| {
            b.iter(|| {
                for (d, s) in dst.iter_mut().zip(src.iter()) {
                    *d = s * 2.0 + 1.0;
                }
                black_box(&dst);
            })
        });
        group.bench_with_input(BenchmarkId::new("rayon_par_iter", n), &n, |b, _| {
            b.iter(|| {
                dst.par_iter_mut().zip(src.par_iter()).for_each(|(d, s)| *d = s * 2.0 + 1.0);
                black_box(&dst);
            })
        });
    }
    group.finish();
}

fn axpy(c: &mut Criterion) {
    let mut group = c.benchmark_group("B4_axpy_f32");
    for &n in &[1_000_000usize, 10_000_000] {
        let mut data: Vec<f32> = (0..n).map(|i| (i % 1000) as f32 / 1000.0).collect();
        group.bench_with_input(BenchmarkId::new("sequential", n), &n, |b, _| {
            b.iter(|| {
                for x in data.iter_mut() {
                    *x = *x * 0.5 + 1.0;
                }
                black_box(&data);
            })
        });
        group.bench_with_input(BenchmarkId::new("rayon_par_iter", n), &n, |b, _| {
            b.iter(|| {
                data.par_iter_mut().for_each(|x| *x = *x * 0.5 + 1.0);
                black_box(&data);
            })
        });
    }
    group.finish();
}

fn sum(c: &mut Criterion) {
    let mut group = c.benchmark_group("B5_sum_f64");
    for &n in &[1_000_000usize, 10_000_000] {
        let data: Vec<f64> = (0..n).map(|i| (i % 1000) as f64 / 1000.0).collect();
        group.bench_with_input(BenchmarkId::new("sequential", n), &n, |b, _| {
            b.iter(|| black_box(data.iter().sum::<f64>()))
        });
        group.bench_with_input(BenchmarkId::new("rayon_par_iter", n), &n, |b, _| {
            b.iter(|| black_box(data.par_iter().sum::<f64>()))
        });
    }
    group.finish();
}

criterion_group!(benches, trivial, axpy, sum);
criterion_main!(benches);
