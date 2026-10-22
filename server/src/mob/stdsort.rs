//! std::sort as libstdc++ (the oracle's GCC) implements it: introsort with a median-of-three pivot moved to the
//! front, heapsort past the depth limit, and a final insertion sort (threshold 16). It is not stable, so wherever
//! rAthena std::sorts elements that compare equal (mob_add_spawn's spawn lists) the resulting order only matches
//! with the same algorithm.

/// `std::sort(v.begin(), v.end(), less)`.
pub fn std_sort<T, F: Fn(&T, &T) -> bool>(v: &mut [T], less: F) {
    let n = v.len();
    if n == 0 {
        return;
    }
    introsort_loop(v, 0, n, lg(n) * 2, &less);
    final_insertion_sort(v, 0, n, &less);
}

const S_THRESHOLD: usize = 16;

/// std::__lg: floor(log2(n)).
fn lg(n: usize) -> usize {
    (usize::BITS - 1 - n.leading_zeros()) as usize
}

fn introsort_loop<T, F: Fn(&T, &T) -> bool>(v: &mut [T], first: usize, mut last: usize, mut depth_limit: usize, less: &F) {
    while last - first > S_THRESHOLD {
        if depth_limit == 0 {
            partial_sort(v, first, last, less);
            return;
        }
        depth_limit -= 1;
        let cut = unguarded_partition_pivot(v, first, last, less);
        introsort_loop(v, cut, last, depth_limit, less);
        last = cut;
    }
}

fn unguarded_partition_pivot<T, F: Fn(&T, &T) -> bool>(v: &mut [T], first: usize, last: usize, less: &F) -> usize {
    let mid = first + (last - first) / 2;
    move_median_to_first(v, first, first + 1, mid, last - 1, less);
    unguarded_partition(v, first + 1, last, first, less)
}

fn move_median_to_first<T, F: Fn(&T, &T) -> bool>(v: &mut [T], result: usize, a: usize, b: usize, c: usize, less: &F) {
    if less(&v[a], &v[b]) {
        if less(&v[b], &v[c]) {
            v.swap(result, b);
        } else if less(&v[a], &v[c]) {
            v.swap(result, c);
        } else {
            v.swap(result, a);
        }
    } else if less(&v[a], &v[c]) {
        v.swap(result, a);
    } else if less(&v[b], &v[c]) {
        v.swap(result, c);
    } else {
        v.swap(result, b);
    }
}

fn unguarded_partition<T, F: Fn(&T, &T) -> bool>(v: &mut [T], mut first: usize, mut last: usize, pivot: usize, less: &F) -> usize {
    loop {
        while less(&v[first], &v[pivot]) {
            first += 1;
        }
        last -= 1;
        while less(&v[pivot], &v[last]) {
            last -= 1;
        }
        if first >= last {
            return first;
        }
        v.swap(first, last);
        first += 1;
    }
}

fn final_insertion_sort<T, F: Fn(&T, &T) -> bool>(v: &mut [T], first: usize, last: usize, less: &F) {
    if last - first > S_THRESHOLD {
        insertion_sort(v, first, first + S_THRESHOLD, less);
        for i in first + S_THRESHOLD..last {
            unguarded_linear_insert(v, i, less);
        }
    } else {
        insertion_sort(v, first, last, less);
    }
}

fn insertion_sort<T, F: Fn(&T, &T) -> bool>(v: &mut [T], first: usize, last: usize, less: &F) {
    if first == last {
        return;
    }
    for i in first + 1..last {
        if less(&v[i], &v[first]) {
            // move_backward(first, i, i + 1); *first = val
            v[first..=i].rotate_right(1);
        } else {
            unguarded_linear_insert(v, i, less);
        }
    }
}

fn unguarded_linear_insert<T, F: Fn(&T, &T) -> bool>(v: &mut [T], mut last: usize, less: &F) {
    // val = *last; while (val < *(last - 1)) { *last = *(last - 1); --last; } *last = val
    while last > 0 && less(&v[last], &v[last - 1]) {
        v.swap(last, last - 1);
        last -= 1;
    }
}

/// std::__partial_sort(first, last, last): __heap_select (make_heap) then __sort_heap.
fn partial_sort<T, F: Fn(&T, &T) -> bool>(v: &mut [T], first: usize, last: usize, less: &F) {
    make_heap(v, first, last, less);
    let mut end = last;
    while end - first > 1 {
        end -= 1;
        // __pop_heap(first, end, end): value = *end; *end = *first; __adjust_heap(first, 0, end - first, value)
        v.swap(first, end);
        adjust_heap(v, first, 0, end - first, less);
    }
}

fn make_heap<T, F: Fn(&T, &T) -> bool>(v: &mut [T], first: usize, last: usize, less: &F) {
    let len = last - first;
    if len < 2 {
        return;
    }
    let mut parent = (len - 2) / 2;
    loop {
        adjust_heap(v, first, parent, len, less);
        if parent == 0 {
            return;
        }
        parent -= 1;
    }
}

/// std::__adjust_heap with the value that sits at `first + hole` (moved down, then pushed back up).
fn adjust_heap<T, F: Fn(&T, &T) -> bool>(v: &mut [T], first: usize, mut hole: usize, len: usize, less: &F) {
    let top = hole;
    let mut child = hole;
    // the value being placed travels with the hole: keep it at `hole` by swapping
    while child < (len.saturating_sub(1)) / 2 {
        child = 2 * (child + 1);
        if less(&v[first + child], &v[first + child - 1]) {
            child -= 1;
        }
        v.swap(first + hole, first + child);
        hole = child;
    }
    if len & 1 == 0 && child == (len - 2) / 2 {
        child = 2 * (child + 1);
        v.swap(first + hole, first + child - 1);
        hole = child - 1;
    }
    // __push_heap(first, hole, top, value)
    let mut parent = if hole > 0 { (hole - 1) / 2 } else { 0 };
    while hole > top && less(&v[first + parent], &v[first + hole]) {
        v.swap(first + hole, first + parent);
        hole = parent;
        parent = if hole > 0 { (hole - 1) / 2 } else { 0 };
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn sorts_and_keeps_libstdcxx_order_of_equals() {
        let mut v: Vec<(u32, u32)> = (0..40).map(|i| ((i * 7919) % 13, i)).collect();
        std_sort(&mut v, |a, b| a.0 > b.0);
        assert!(v.windows(2).all(|w| w[0].0 >= w[1].0));
        let mut ids: Vec<u32> = v.iter().map(|e| e.1).collect();
        ids.sort();
        assert_eq!(ids, (0..40).collect::<Vec<_>>());
        // up to 16 elements std::sort is an insertion sort: stable
        let mut w: Vec<(u32, u32)> = vec![(1, 0), (2, 1), (1, 2), (2, 3)];
        std_sort(&mut w, |a, b| a.0 > b.0);
        assert_eq!(w, vec![(2, 1), (2, 3), (1, 0), (1, 2)]);
    }
}
