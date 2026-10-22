//! The language core: variables and arrays, dynamic variables, user functions and subroutines, flow control,
//! arithmetic helpers, time, sleeping, attaching players (script.cpp BUILDIN_FUNCs of the same names).

use super::*;
use crate::script::reg;
use crate::script::vm::*;

pub fn lookup(cfunc: &str) -> Option<BuildinFn> {
    Some(match cfunc {
        "setr" => setr,
        "setarray" => setarray,
        "cleararray" => cleararray,
        "copyarray" => copyarray,
        "getarraysize" => getarraysize,
        "deletearray" => deletearray,
        "getelementofarray" => getelementofarray,
        "inarray" => inarray,
        "countinarray" => countinarray,
        "getd" => getd,
        "setd" => setd,
        "getvariableofnpc" => getvariableofnpc,
        "callfunc" => callfunc,
        "callsub" => callsub,
        "getarg" => getarg,
        "getargcount" => getargcount,
        "return" => r#return,
        "goto" => goto,
        "jump_zero" => jump_zero,
        "end" => end,
        "rand" => rand,
        "minmax" => minmax,
        "cap_value" => cap_value,
        "pow" => pow,
        "sqrt" => sqrt,
        "distance" => distance,
        "round" => round,
        "freeloop" => freeloop,
        "is_function" => is_function,
        "gettimetick" => gettimetick,
        "gettime" => gettime,
        "gettimestr" => gettimestr,
        "sleep" => sleep,
        "sleep2" => sleep2,
        "awake" => awake,
        "debugmes" => debugmes,
        "errormes" => errormes,
        "playerattached" => playerattached,
        "getattachedrid" => getattachedrid,
        "attachrid" => attachrid,
        "detachrid" => detachrid,
        _ => return None,
    })
}

fn not_a_variable(rt: &Rt, st: &mut State, what: &str) -> i32 {
    log::error!("script:{what}: not a variable");
    reportdata(&st.data(2).clone());
    st.state = END;
    let _ = rt;
    SCRIPT_CMD_FAILURE
}

/// The (id, index, container, name) of a reference argument.
fn reference(st: &State, i: i32) -> Option<(i32, u32, RegRef, Vec<u8>)> {
    match st.data(i) {
        Data::Name(id, idx, rf) => Some((*id, *idx, rf.clone(), name_of(*id))),
        _ => None,
    }
}

/// set / setr.
fn setr(rt: &Rt, st: &mut State) -> i32 {
    let Some((id, idx, rf, name)) = reference(st, 2) else {
        log::error!("script:set: not a variable");
        reportdata(&st.data(2).clone());
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let command = st.funcname.clone();
    let pos = if command == "setr" { 5 } else { 4 };
    let prefix = name.first().copied().unwrap_or(0);
    let mut sd = None;
    if not_server_variable(prefix) {
        sd = charid2sd(rt, st, pos, "buildin_setr");
        if sd.is_none() {
            log::error!("buildin_set: No player attached for player variable '{}'", lat(&name));
            return SCRIPT_CMD_FAILURE;
        }
    }
    if command == "setr" && st.hasdata(4) {
        // post-increment / post-decrement: return the previous value
        if is_string_variable(&name) {
            let v = getstr(rt, st, 4);
            st.pushstr(v);
        } else {
            let v = getnum64(rt, st, 4);
            st.pushint(v);
        }
    } else {
        st.pushcopy(2);
    }
    if is_string_variable(&name) {
        let v = getstr(rt, st, 3);
        reg::set_reg_str(rt, st, sd, id, idx, &name, &v, &rf);
    } else {
        let v = getnum64(rt, st, 3);
        reg::set_reg_num(rt, st, sd, id, idx, &name, v, &rf);
    }
    SCRIPT_CMD_SUCCESS
}

fn player_for(rt: &Rt, st: &mut State, name: &[u8], func: &str) -> Result<Option<u32>, ()> {
    if not_server_variable(name.first().copied().unwrap_or(0)) {
        match rid2sd(rt, st, func) {
            Some(a) => Ok(Some(a)),
            None => Err(()),
        }
    } else {
        Ok(None)
    }
}

/// setarray.
fn setarray(rt: &Rt, st: &mut State) -> i32 {
    let Some((id, mut start, rf, name)) = reference(st, 2) else { return not_a_variable(rt, st, "setarray") };
    let Ok(sd) = player_for(rt, st, &name, "buildin_setarray") else { return SCRIPT_CMD_SUCCESS };
    let mut end = start.wrapping_add((st.lastdata() - 2) as u32);
    if end > SCRIPT_MAX_ARRAYSIZE {
        end = SCRIPT_MAX_ARRAYSIZE;
    }
    let mut i = 3;
    if is_string_variable(&name) {
        while start < end {
            let v = getstr(rt, st, i);
            reg::set_reg_str(rt, st, sd, id, start, &name, &v, &rf);
            start += 1;
            i += 1;
        }
    } else {
        while start < end {
            let v = getnum64(rt, st, i);
            reg::set_reg_num(rt, st, sd, id, start, &name, v, &rf);
            start += 1;
            i += 1;
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// cleararray.
fn cleararray(rt: &Rt, st: &mut State) -> i32 {
    let Some((id, mut start, rf, name)) = reference(st, 2) else { return not_a_variable(rt, st, "cleararray") };
    let Ok(sd) = player_for(rt, st, &name, "buildin_cleararray") else { return SCRIPT_CMD_SUCCESS };
    let mut end = start.wrapping_add(getnum(rt, st, 4) as u32);
    if end > SCRIPT_MAX_ARRAYSIZE {
        end = SCRIPT_MAX_ARRAYSIZE;
    }
    if is_string_variable(&name) {
        let v = getstr(rt, st, 3);
        while start < end {
            reg::set_reg_str(rt, st, sd, id, start, &name, &v, &rf);
            start += 1;
        }
    } else {
        let v = getnum64(rt, st, 3);
        while start < end {
            reg::set_reg_num(rt, st, sd, id, start, &name, v, &rf);
            start += 1;
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// copyarray.
fn copyarray(rt: &Rt, st: &mut State) -> i32 {
    let (Some((id1, idx1, rf1, name1)), Some((id2, idx2, rf2, name2))) = (reference(st, 2), reference(st, 3)) else {
        log::error!("script:copyarray: not a variable");
        reportdata(&st.data(2).clone());
        reportdata(&st.data(3).clone());
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let is_string = is_string_variable(&name1);
    if is_string != is_string_variable(&name2) {
        log::error!("script:copyarray: type mismatch");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let mut sd = None;
    if not_server_variable(name1[0]) || not_server_variable(name2[0]) {
        sd = rid2sd(rt, st, "buildin_copyarray");
        if sd.is_none() {
            return SCRIPT_CMD_SUCCESS;
        }
    }
    let mut count = getnum(rt, st, 4) as u32;
    if count > SCRIPT_MAX_ARRAYSIZE - idx1 {
        count = SCRIPT_MAX_ARRAYSIZE - idx1;
    }
    if count == 0 || (id1 == id2 && idx1 == idx2) {
        return SCRIPT_CMD_SUCCESS;
    }
    let copy = |st: &mut State, i: u32| {
        if is_string {
            let v = get_val2_str(rt, st, id2, idx2.wrapping_add(i), &rf2);
            reg::set_reg_str(rt, st, sd, id1, idx1.wrapping_add(i), &name1, &v, &rf1);
        } else {
            let v = get_val2_num(rt, st, id2, idx2.wrapping_add(i), &rf2);
            reg::set_reg_num(rt, st, sd, id1, idx1.wrapping_add(i), &name1, v, &rf1);
        }
    };
    if id1 == id2 && idx1 > idx2 {
        // destination might overlap the source: copy in reverse order
        let mut i = count as i64 - 1;
        while i >= 0 {
            copy(st, i as u32);
            i -= 1;
        }
    } else {
        for i in 0..count {
            if (idx2 as u64 + i as u64) < SCRIPT_MAX_ARRAYSIZE as u64 {
                copy(st, i);
            } else {
                reg::clear_reg(rt, st, sd, id1, idx1.wrapping_add(i), &name1, &rf1);
            }
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// getarraysize.
fn getarraysize(rt: &Rt, st: &mut State) -> i32 {
    let Some((_, _, rf, name)) = reference(st, 2) else {
        log::error!("script:getarraysize: not a variable");
        reportdata(&st.data(2).clone());
        st.pushnil();
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let Ok(sd) = player_for(rt, st, &name, "buildin_getarraysize") else { return SCRIPT_CMD_SUCCESS };
    let n = reg::highest_key(rt, st, sd, &name, &rf);
    st.pushint(n as i64);
    SCRIPT_CMD_SUCCESS
}

/// deletearray.
fn deletearray(rt: &Rt, st: &mut State) -> i32 {
    let Some((id, mut start, rf, name)) = reference(st, 2) else { return not_a_variable(rt, st, "deletearray") };
    let Ok(sd) = player_for(rt, st, &name, "buildin_deletearray") else { return SCRIPT_CMD_SUCCESS };
    let Some(list) = reg::members(rt, st, sd, &name, &rf) else {
        log::error!("script:deletearray: not a array");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    if list.is_empty() {
        return SCRIPT_CMD_SUCCESS; // non-existent array, nothing to empty
    }
    let end = reg::highest_key(rt, st, sd, &name, &rf);
    if start >= end {
        return SCRIPT_CMD_SUCCESS;
    }
    let is_string = is_string_variable(&name);
    if st.hasdata(3) {
        let mut count = getnum(rt, st, 3) as u32;
        if count > end - start {
            count = end - start;
        }
        if count == 0 {
            return SCRIPT_CMD_SUCCESS;
        }
        if ((end - start) as usize) < list.len() {
            // compact and overwrite
            while start + count < end {
                if is_string {
                    let v = get_val2_str(rt, st, id, start + count, &rf);
                    reg::set_reg_str(rt, st, sd, id, start, &name, &v, &rf);
                } else {
                    let v = get_val2_num(rt, st, id, start + count, &rf);
                    reg::set_reg_num(rt, st, sd, id, start, &name, v, &rf);
                }
                start += 1;
            }
            while start < end {
                reg::clear_reg(rt, st, sd, id, start, &name, &rf);
                start += 1;
            }
        } else {
            // using the member list (sorted)
            let mut i = list.iter().position(|&m| m >= start).unwrap_or(list.len());
            while i < list.len() && list[i] < start + count {
                reg::clear_reg(rt, st, sd, id, list[i], &name, &rf);
                i += 1;
            }
            while i < list.len() && list[i] < end {
                if is_string {
                    let v = get_val2_str(rt, st, id, list[i], &rf);
                    reg::set_reg_str(rt, st, sd, id, list[i] - count, &name, &v, &rf);
                } else {
                    let v = get_val2_num(rt, st, id, list[i], &rf);
                    reg::set_reg_num(rt, st, sd, id, list[i] - count, &name, v, &rf);
                }
                reg::clear_reg(rt, st, sd, id, list[i], &name, &rf);
                i += 1;
            }
        }
    } else {
        for m in list {
            if m >= start {
                reg::clear_reg(rt, st, sd, id, m, &name, &rf);
            }
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// getelementofarray.
fn getelementofarray(rt: &Rt, st: &mut State) -> i32 {
    let Some((id, _, rf, _)) = reference(st, 2) else {
        log::error!("script:getelementofarray: not a variable");
        reportdata(&st.data(2).clone());
        st.pushnil();
        st.state = END;
        return SCRIPT_CMD_SUCCESS;
    };
    let i = getnum(rt, st, 3) as i64;
    if i < 0 || i >= SCRIPT_MAX_ARRAYSIZE as i64 {
        log::warn!("script:getelementofarray: index out of range ({i})");
        reportdata(&st.data(2).clone());
        st.pushnil();
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    st.push(Data::Name(id, i as u32, rf));
    SCRIPT_CMD_SUCCESS
}

/// inarray.
fn inarray(rt: &Rt, st: &mut State) -> i32 {
    let Some((id, _, rf, name)) = reference(st, 2) else {
        log::error!("buildin_inarray: not a variable");
        reportdata(&st.data(2).clone());
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let mut sd = None;
    if not_server_variable(name[0]) {
        sd = rid2sd(rt, st, "buildin_inarray");
        if sd.is_none() {
            return SCRIPT_CMD_FAILURE;
        }
    }
    let size = reg::highest_key(rt, st, sd, &name, &rf);
    if size == 0 {
        st.pushint(-1);
        return SCRIPT_CMD_SUCCESS;
    }
    if size >= SCRIPT_MAX_ARRAYSIZE {
        log::error!("buildin_inarray: The array is too large.");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    if is_string_variable(&name) {
        let value = getstr(rt, st, 3);
        for i in 0..size {
            if get_val2_str(rt, st, id, i, &rf) == value {
                st.pushint(i as i64);
                return SCRIPT_CMD_SUCCESS;
            }
        }
    } else {
        let value = getnum64(rt, st, 3);
        for i in 0..size {
            if get_val2_num(rt, st, id, i, &rf) == value {
                st.pushint(i as i64);
                return SCRIPT_CMD_SUCCESS;
            }
        }
    }
    st.pushint(-1);
    SCRIPT_CMD_SUCCESS
}

/// countinarray.
fn countinarray(rt: &Rt, st: &mut State) -> i32 {
    let (Some((id1, i1, rf1, name1)), Some((id2, j2, rf2, name2))) = (reference(st, 2), reference(st, 3)) else {
        log::error!("buildin_countinarray: not a variable");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let mut sd = st.rid.ne(&0).then_some(st.rid);
    if not_server_variable(name1[0]) && not_server_variable(name2[0]) {
        sd = rid2sd(rt, st, "buildin_countinarray");
        if sd.is_none() {
            return SCRIPT_CMD_FAILURE;
        }
    }
    let size1 = reg::highest_key(rt, st, sd, &name1, &rf1);
    let size2 = reg::highest_key(rt, st, sd, &name2, &rf2);
    if size1 == 0 || size2 == 0 {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    }
    if size1 >= SCRIPT_MAX_ARRAYSIZE || size2 >= SCRIPT_MAX_ARRAYSIZE {
        log::error!("buildin_countinarray: The array is too large.");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    if i1 > size1 - 1 || j2 > size2 - 1 {
        log::error!("buildin_countinarray: The given index of the array is higher than the array size.");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let mut count = 0i64;
    match (is_string_variable(&name1), is_string_variable(&name2)) {
        (true, true) => {
            for i in i1..size1 {
                let a = get_val2_str(rt, st, id1, i, &rf1);
                for j in j2..size2 {
                    if a == get_val2_str(rt, st, id2, j, &rf2) {
                        count += 1;
                    }
                }
            }
        }
        (false, false) => {
            for i in i1..size1 {
                let a = get_val2_num(rt, st, id1, i, &rf1);
                for j in j2..size2 {
                    if a == get_val2_num(rt, st, id2, j, &rf2) {
                        count += 1;
                    }
                }
            }
        }
        _ => {
            log::error!("buildin_countinarray: Arrays do not match , You can't compare an int32 array to a string array.");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
    }
    st.pushint(count);
    SCRIPT_CMD_SUCCESS
}

/// sscanf(buffer, "%99[^[][%11d]", varname, &elem): (varname, elem or 0).
fn parse_dynamic_name(buf: &[u8]) -> (Vec<u8>, i32) {
    let name: Vec<u8> = buf.iter().copied().take_while(|&b| b != b'[').take(99).collect();
    if name.is_empty() {
        return (name, 0);
    }
    let rest = &buf[name.len()..];
    if rest.first() != Some(&b'[') {
        return (name, 0);
    }
    let digits = &rest[1..];
    let skip = digits.iter().take_while(|&&b| crate::script::compiler::is_space(b)).count();
    let d = &digits[skip..];
    let mut n = 0;
    if !d.is_empty() && (d[0] == b'-' || d[0] == b'+') {
        n = 1;
    }
    while n < d.len() && n < 11 && d[n].is_ascii_digit() {
        n += 1;
    }
    let digits_only = d[..n].iter().filter(|b| b.is_ascii_digit()).count();
    if digits_only == 0 {
        return (name, 0);
    }
    let (v, _, _) = crate::script::compiler::strtoll(&d[..n], 0, 10);
    (name, v as i32)
}

/// getd.
fn getd(rt: &Rt, st: &mut State) -> i32 {
    let buf = getstr(rt, st, 2);
    let (name, elem) = parse_dynamic_name(&buf);
    let id = add_str(&name);
    st.push(Data::Name(id, elem as u32, RegRef::None));
    SCRIPT_CMD_SUCCESS
}

/// setd.
fn setd(rt: &Rt, st: &mut State) -> i32 {
    let buf = getstr(rt, st, 2);
    let (name, elem) = parse_dynamic_name(&buf);
    let mut sd = None;
    if not_server_variable(name.first().copied().unwrap_or(0)) {
        sd = charid2sd(rt, st, 4, "buildin_setd");
        if sd.is_none() {
            return SCRIPT_CMD_FAILURE;
        }
    }
    let id = add_str(&name);
    if is_string_variable(&name) {
        let v = getstr(rt, st, 3);
        reg::set_reg_str(rt, st, sd, id, elem as u32, &name, &v, &RegRef::None);
    } else {
        let v = getnum64(rt, st, 3);
        reg::set_reg_num(rt, st, sd, id, elem as u32, &name, v, &RegRef::None);
    }
    SCRIPT_CMD_SUCCESS
}

/// getvariableofnpc.
fn getvariableofnpc(rt: &Rt, st: &mut State) -> i32 {
    let Some((id, idx, _, name)) = reference(st, 2) else {
        log::error!("buildin_getvariableofnpc: not a variable");
        reportdata(&st.data(2).clone());
        st.pushnil();
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    if name.first() != Some(&b'.') || name.get(1) == Some(&b'@') {
        log::error!("buildin_getvariableofnpc: invalid scope (not npc variable)");
        st.pushnil();
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let npc = getstr(rt, st, 3);
    let key = rt.c.and_then(|c| crate::script::npc_run::npc_name2id(c, &lat(&npc)).and_then(|nd| crate::script::code::npc_code(c, nd.id)).map(|(k, _)| k));
    let Some(key) = key else {
        log::error!("buildin_getvariableofnpc: can't find npc {}", lat(&npc));
        st.pushnil();
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    st.push(Data::Name(id, idx, RegRef::Local(key)));
    SCRIPT_CMD_SUCCESS
}

/// callfunc.
fn callfunc(rt: &Rt, st: &mut State) -> i32 {
    let name = getstr(rt, st, 2);
    let fname = lat(&name);
    let key = CodeKey::Func(fname.clone());
    let exists = crate::script::code::userfunc_exists(rt.c, &fname);
    let code = if exists { crate::script::code::get(rt.c, &key) } else { None };
    let Some(code) = code else {
        log::error!("buildin_callfunc: Function not found! [{fname}]");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let scope_ref = RegRef::Scope(st.cur_scope);
    let local_ref = RegRef::Local(st.code.clone());
    let mut j = 0;
    for i in st.start + 3..st.end {
        st.push_copy(i);
        let k = st.stack.len() - 1;
        if let Data::Name(id, _, rf) = &mut st.stack[k] {
            if *rf == RegRef::None {
                let n = name_of(*id);
                if n.first() == Some(&b'.') {
                    *rf = if n.get(1) == Some(&b'@') { scope_ref.clone() } else { local_ref.clone() };
                }
            }
        }
        j += 1;
    }
    let ri = RetInfo { scope: st.cur_scope, code: st.code.clone(), pos: st.pos, nargs: j, defsp: st.defsp };
    st.push(Data::RetInfo(ri));
    st.pos = 0;
    st.code = key;
    st.buf = code;
    st.defsp = st.sp();
    st.state = GOTO;
    st.cur_scope = st.new_scope();
    SCRIPT_CMD_SUCCESS
}

/// callsub.
fn callsub(rt: &Rt, st: &mut State) -> i32 {
    let pos = getnum(rt, st, 2);
    if !st.data(2).is_label() {
        log::error!("buildin_callsub: Argument is not a label");
        reportdata(&st.data(2).clone());
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let scope_ref = RegRef::Scope(st.cur_scope);
    let mut j = 0;
    for i in st.start + 3..st.end {
        st.push_copy(i);
        let k = st.stack.len() - 1;
        if let Data::Name(id, _, rf) = &mut st.stack[k] {
            if *rf == RegRef::None {
                let n = name_of(*id);
                if n.first() == Some(&b'.') && n.get(1) == Some(&b'@') {
                    *rf = scope_ref.clone();
                }
            }
        }
        j += 1;
    }
    let ri = RetInfo { scope: st.cur_scope, code: st.code.clone(), pos: st.pos, nargs: j, defsp: st.defsp };
    st.push(Data::RetInfo(ri));
    st.pos = pos;
    st.defsp = st.sp();
    st.state = GOTO;
    st.cur_scope = st.new_scope();
    SCRIPT_CMD_SUCCESS
}

fn current_retinfo(st: &State) -> Option<RetInfo> {
    if st.defsp < 1 {
        return None;
    }
    match &st.stack[(st.defsp - 1) as usize] {
        Data::RetInfo(ri) => Some(ri.clone()),
        _ => None,
    }
}

/// getarg.
fn getarg(rt: &Rt, st: &mut State) -> i32 {
    let Some(ri) = current_retinfo(st) else {
        log::error!("buildin_getarg: No callfunc or callsub!");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    let idx = getnum(rt, st, 2);
    if idx >= 0 && idx < ri.nargs {
        st.push_copy(st.defsp - 1 - ri.nargs + idx);
    } else if st.hasdata(3) {
        st.pushcopy(3);
    } else {
        log::error!("buildin_getarg: Index (idx={idx}) out of range (nargs={}) and no default value found", ri.nargs);
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    SCRIPT_CMD_SUCCESS
}

/// getargcount.
fn getargcount(_rt: &Rt, st: &mut State) -> i32 {
    let Some(ri) = current_retinfo(st) else {
        log::error!("script:getargcount: used out of function or callsub label!");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    };
    st.pushint(ri.nargs as i64);
    SCRIPT_CMD_SUCCESS
}

/// return.
fn r#return(rt: &Rt, st: &mut State) -> i32 {
    if st.hasdata(2) {
        st.pushcopy(2);
        let k = st.stack.len() - 1;
        if let Data::Name(id, _, rf) = st.stack[k].clone() {
            let n = name_of(id);
            if n.first() == Some(&b'.') && n.get(1) == Some(&b'@') {
                let cur = st.cur_scope;
                if rf == RegRef::None || rf == RegRef::Scope(cur) {
                    get_val(rt, st, k); // current scope: convert to value
                }
                if let Data::Name(_, _, rf2) = &st.stack[k] {
                    let parent = current_retinfo(st).map(|r| r.scope);
                    if let (RegRef::Scope(s), Some(p)) = (rf2, parent) {
                        if *s == p {
                            if let Data::Name(_, _, r) = &mut st.stack[k] {
                                *r = RegRef::None; // reference to the parent scope
                            }
                        }
                    }
                }
            }
        }
    } else {
        st.pushnil();
    }
    st.state = RETFUNC;
    SCRIPT_CMD_SUCCESS
}

/// goto.
fn goto(rt: &Rt, st: &mut State) -> i32 {
    if !st.data(2).is_label() {
        log::error!("buildin_goto: Not a label");
        reportdata(&st.data(2).clone());
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    st.pos = getnum(rt, st, 2);
    st.state = GOTO;
    SCRIPT_CMD_SUCCESS
}

/// jump_zero.
fn jump_zero(rt: &Rt, st: &mut State) -> i32 {
    let sel = getnum64(rt, st, 2);
    if sel == 0 {
        if !st.data(3).is_label() {
            log::error!("script: jump_zero: not label !");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
        st.pos = getnum(rt, st, 3);
        st.state = GOTO;
    }
    SCRIPT_CMD_SUCCESS
}

/// end.
fn end(rt: &Rt, st: &mut State) -> i32 {
    st.state = END;
    if let Some(c) = rt.c {
        // a dynamic NPC's last interaction (npc_load.rs)
        crate::npc_load::dynamicnpc_touch(c, st.oid);
    }
    st.mes_active = false;
    if let Some(c) = rt.c {
        if st.rid != 0 {
            if let Some(sd) = crate::unit::get(c, st.rid).filter(|_| crate::pc::get(c, st.rid).is_some()) {
                let mut x = crate::script::npc_run::sdx(c, st.rid);
                if !x.callshop {
                    // if a menu/select/prompt is active, close it
                    crate::clif::scriptclose(c, &sd, st.oid);
                } else {
                    x.callshop = false;
                    crate::script::npc_run::put_sdx(c, &x);
                }
            }
        }
    }
    SCRIPT_CMD_SUCCESS
}

/// rnd_value(min, max) for int64 (swapping reversed bounds).
pub fn rnd_value64(rt: &Rt, min: i64, max: i64) -> i64 {
    match rt.c {
        Some(c) => c.rnd_value_i64(min, max),
        None => if min > max { max } else { min },
    }
}

/// rand.
fn rand(rt: &Rt, st: &mut State) -> i32 {
    let (min, mut max);
    if st.hasdata(3) {
        min = getnum64(rt, st, 2);
        max = getnum64(rt, st, 3);
        if min > max {
            log::warn!("buildin_rand: minimum ({min}) is bigger than maximum ({max}).");
        }
    } else {
        min = 0;
        max = getnum64(rt, st, 2);
        if max < 0 {
            log::error!("buildin_rand: range ({max}) is negative.");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
        max -= 1;
        if max < 1 {
            log::error!("buildin_rand: range ({max}) is too small. No randomness possible.");
            st.state = END;
            return SCRIPT_CMD_FAILURE;
        }
    }
    if min == max {
        log::error!("buildin_rand: minimum ({min}) and maximum ({max}) are equal. No randomness possible.");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let v = rnd_value64(rt, min, max);
    st.pushint(v);
    SCRIPT_CMD_SUCCESS
}

/// min / minimum / max / maximum.
fn minmax(rt: &Rt, st: &mut State) -> i32 {
    let fname = st.funcname.to_ascii_lowercase();
    let mut i = 2;
    if !st.hasdata(i) {
        log::error!("buildin_{fname}: no arguments given!");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let is_min = fname.starts_with("min");
    if !is_min && !fname.starts_with("max") {
        log::error!("buildin_{fname}: Unknown call case for min/max!");
        st.state = END;
        return SCRIPT_CMD_FAILURE;
    }
    let f = |a: i64, b: i64| if is_min { a.min(b) } else { a.max(b) };
    let mut value = if is_min { i64::MAX } else { i64::MIN };
    while st.hasdata(i) {
        match st.data(i).clone() {
            Data::Int(_) => {
                // script_getnum: int32
                let v = getnum(rt, st, i) as i64;
                value = f(value, v);
            }
            Data::Name(id, start, rf) => {
                let name = name_of(id);
                if is_string_variable(&name) {
                    log::error!("buildin_{fname}: illegal type, need integer!");
                    st.state = END;
                    return SCRIPT_CMD_FAILURE;
                }
                let mut sd = if st.rid != 0 { Some(st.rid) } else { None };
                if not_server_variable(name[0]) {
                    sd = rid2sd(rt, st, "buildin_minmax");
                    if sd.is_none() {
                        log::error!("buildin_{fname}: Cannot use a player variable '{}' if no player is attached.", lat(&name));
                        return SCRIPT_CMD_FAILURE;
                    }
                }
                if reg::store_for(rt, st, sd, &name, &rf).is_none() {
                    log::error!("buildin_{fname}: not a array!");
                    st.state = END;
                    return SCRIPT_CMD_FAILURE;
                }
                let end = reg::highest_key(rt, st, sd, &name, &rf);
                if start < end {
                    for k in start..end {
                        let v = get_val2_num(rt, st, id, k, &rf);
                        value = f(value, v);
                    }
                } else {
                    value = f(value, 0);
                }
            }
            d => {
                log::error!("buildin_{fname}: not a supported data type!");
                reportdata(&d);
                st.state = END;
                return SCRIPT_CMD_FAILURE;
            }
        }
        i += 1;
    }
    st.pushint(value);
    SCRIPT_CMD_SUCCESS
}

/// cap_value.
fn cap_value(rt: &Rt, st: &mut State) -> i32 {
    let v = getnum64(rt, st, 2);
    let lo = getnum64(rt, st, 3);
    let hi = getnum64(rt, st, 4);
    // cbasetypes.hpp cap_value: (a < first ? first : a > last ? last : a)
    let r = if v < lo {
        lo
    } else if v > hi {
        hi
    } else {
        v
    };
    st.pushint(r);
    SCRIPT_CMD_SUCCESS
}

/// (int32) of a double the way x86 converts out-of-range values (0x80000000).
fn d2i(v: f64) -> i32 {
    if v.is_nan() || v >= 2_147_483_648.0 || v < -2_147_483_648.0 {
        i32::MIN
    } else {
        v as i32
    }
}

/// pow.
fn pow(rt: &Rt, st: &mut State) -> i32 {
    let a = getnum(rt, st, 2) as f64;
    let b = getnum(rt, st, 3) as f64;
    st.pushint(d2i(a.powf(b)) as i64);
    SCRIPT_CMD_SUCCESS
}

/// sqrt.
fn sqrt(rt: &Rt, st: &mut State) -> i32 {
    let i = getnum(rt, st, 2) as f64;
    st.pushint(d2i(i.sqrt()) as i64);
    SCRIPT_CMD_SUCCESS
}

/// distance: distance_xy with int16 coordinates.
fn distance(rt: &Rt, st: &mut State) -> i32 {
    let x0 = getnum(rt, st, 2) as i16 as i32;
    let y0 = getnum(rt, st, 3) as i16 as i32;
    let x1 = getnum(rt, st, 4) as i16 as i32;
    let y1 = getnum(rt, st, 5) as i16 as i32;
    st.pushint(crate::path::distance(x0 - x1, y0 - y1) as i64);
    SCRIPT_CMD_SUCCESS
}

/// round / ceil / floor.
fn round(rt: &Rt, st: &mut State) -> i32 {
    let num = getnum(rt, st, 2);
    let precision = getnum(rt, st, 3);
    let func = st.funcname.to_ascii_lowercase();
    if precision <= 0 {
        log::error!("buildin_round: Attempted to use zero or negative number as arbitrary precision.");
        return SCRIPT_CMD_FAILURE;
    }
    let v = if func == "floor" {
        num.wrapping_sub(num % precision)
    } else if func == "ceil" {
        num.wrapping_add(precision).wrapping_sub(num % precision)
    } else {
        // round() of a double: half away from zero
        (d2i((num as f64 / precision as f64).round())).wrapping_mul(precision)
    };
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

/// freeloop.
fn freeloop(rt: &Rt, st: &mut State) -> i32 {
    if st.hasdata(2) {
        st.freeloop = getnum(rt, st, 2) != 0;
    }
    st.pushint(st.freeloop as i64);
    SCRIPT_CMD_SUCCESS
}

/// is_function.
fn is_function(rt: &Rt, st: &mut State) -> i32 {
    let name = getstr(rt, st, 2);
    let v = crate::script::code::userfunc_exists(rt.c, &lat(&name));
    st.pushint(v as i64);
    SCRIPT_CMD_SUCCESS
}

fn now_secs(rt: &Rt) -> i64 {
    rt.c.map(|c| c.tick().div_euclid(1000)).unwrap_or(0)
}

/// gettimetick.
fn gettimetick(rt: &Rt, st: &mut State) -> i32 {
    let t = getnum(rt, st, 2);
    match t {
        2 => st.pushint(now_secs(rt) as i32 as i64),
        1 => {
            let tm = crate::script::npc_run::localtime(now_secs(rt));
            st.pushint((tm.hour * 3600 + tm.min * 60 + tm.sec) as i64);
        }
        _ => st.pushint(rt.c.map(|c| c.tick()).unwrap_or(0)),
    }
    SCRIPT_CMD_SUCCESS
}

/// date.cpp date_get.
pub fn date_get(rt: &Rt, kind: i32) -> i32 {
    let tm = crate::script::npc_run::localtime(now_secs(rt));
    match kind {
        1 => tm.sec,
        2 => tm.min,
        3 => tm.hour,
        4 => tm.wday,
        5 => tm.mday,
        6 => tm.mon + 1,
        7 => tm.year + 1900,
        8 => tm.yday,
        9 => (tm.year + 1900) * 10000 + (tm.mon + 1) * 100 + tm.mday,
        _ => -1,
    }
}

/// gettime.
fn gettime(rt: &Rt, st: &mut State) -> i32 {
    let t = getnum(rt, st, 2);
    // DT_MIN 0 < type < DT_MAX 10
    if t <= 0 || t >= 10 {
        log::error!("buildin_gettime: Invalid date type {t}");
        reportsrc(rt, st);
        st.pushint(-1);
    } else {
        st.pushint(date_get(rt, t) as i64);
    }
    SCRIPT_CMD_SUCCESS
}

/// gettimestr.
fn gettimestr(rt: &Rt, st: &mut State) -> i32 {
    let fmt = getstr(rt, st, 2);
    let maxlen = getnum(rt, st, 3);
    let now = if st.hasdata(4) {
        let t = getnum(rt, st, 4);
        if t < 0 {
            log::warn!("buildin_gettimestr: a positive value must be supplied to be valid.");
            return SCRIPT_CMD_FAILURE;
        }
        t as i64
    } else {
        now_secs(rt)
    };
    let tm = crate::script::npc_run::localtime(now);
    let s = crate::script::cfmt::strftime(&fmt, maxlen.max(0) as usize, &tm);
    st.pushstr(s);
    SCRIPT_CMD_SUCCESS
}

/// sleep: detaches the player and waits.
fn sleep(rt: &Rt, st: &mut State) -> i32 {
    if st.sleep_tick == 0 {
        let ticks = getnum(rt, st, 2);
        if ticks <= 0 {
            log::error!("buildin_sleep: negative or zero amount('{ticks}') of milli seconds is not supported");
            return SCRIPT_CMD_FAILURE;
        }
        if let Some(c) = rt.c {
            crate::script::npc_run::script_detach_rid(c, st);
        } else {
            st.rid = 0;
        }
        st.state = RERUNLINE;
        st.sleep_tick = ticks;
    } else {
        st.state = RUN;
        st.sleep_tick = 0;
    }
    SCRIPT_CMD_SUCCESS
}

/// sleep2: waits, keeping the unit attached.
fn sleep2(rt: &Rt, st: &mut State) -> i32 {
    let unit_there = |st: &State| rt.c.map(|c| crate::unit::get(c, st.rid).is_some()).unwrap_or(false);
    if st.sleep_tick == 0 {
        let ticks = getnum(rt, st, 2);
        if ticks <= 0 {
            log::error!("buildin_sleep2: negative or zero amount('{ticks}') of milli seconds is not supported");
            return SCRIPT_CMD_FAILURE;
        }
        if !unit_there(st) {
            log::error!("buildin_sleep2: no unit is attached");
            return SCRIPT_CMD_FAILURE;
        }
        st.state = RERUNLINE;
        st.sleep_tick = ticks;
    } else if !unit_there(st) {
        st.rid = 0;
        st.state = END;
    } else {
        st.state = RUN;
        st.sleep_tick = 0;
    }
    SCRIPT_CMD_SUCCESS
}

/// awake.
fn awake(rt: &Rt, st: &mut State) -> i32 {
    let Some(c) = rt.c else { return SCRIPT_CMD_FAILURE };
    let name = getstr(rt, st, 2);
    let Some(nd) = crate::script::npc_run::npc_name2id(c, &lat(&name)) else {
        log::error!("buildin_awake: NPC \"{}\" not found", lat(&name));
        return SCRIPT_CMD_FAILURE;
    };
    let rid = st.rid;
    if rid != 0 {
        crate::script::npc_run::script_detach_rid(c, st);
    }
    crate::script::npc_run::awake_npc(c, nd.id);
    if rid != 0 {
        st.rid = rid;
        crate::script::npc_run::script_attach_state(c, st);
    }
    SCRIPT_CMD_SUCCESS
}

/// debugmes.
fn debugmes(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    log::debug!("script debug : {} {} : {}", st.rid, st.oid, lat(&s));
    SCRIPT_CMD_SUCCESS
}

/// errormes.
fn errormes(rt: &Rt, st: &mut State) -> i32 {
    let s = getstr(rt, st, 2);
    log::error!("{}", lat(&s));
    reportsrc(rt, st);
    SCRIPT_CMD_SUCCESS
}

/// playerattached.
fn playerattached(rt: &Rt, st: &mut State) -> i32 {
    let there = st.rid != 0 && rt.c.map(|c| crate::pc::get(c, st.rid).is_some()).unwrap_or(false);
    st.pushint(if there { st.rid as i64 } else { 0 });
    SCRIPT_CMD_SUCCESS
}

/// getattachedrid.
fn getattachedrid(_rt: &Rt, st: &mut State) -> i32 {
    st.pushint(st.rid as i64);
    SCRIPT_CMD_SUCCESS
}

/// attachrid.
fn attachrid(rt: &Rt, st: &mut State) -> i32 {
    let rid = getnum(rt, st, 2) as u32;
    let force = if st.hasdata(3) { getnum(rt, st, 3) != 0 } else { true };
    let Some(c) = rt.c else {
        st.pushint(0);
        return SCRIPT_CMD_SUCCESS;
    };
    match crate::pc::get(c, rid) {
        Some(p) if p.npc_id == 0 || force => {
            crate::script::npc_run::script_detach_rid(c, st);
            st.rid = rid;
            crate::script::npc_run::script_attach_state(c, st);
            st.pushint(1);
        }
        _ => st.pushint(0),
    }
    SCRIPT_CMD_SUCCESS
}

/// detachrid.
fn detachrid(rt: &Rt, st: &mut State) -> i32 {
    if let Some(c) = rt.c {
        crate::script::npc_run::script_detach_rid(c, st);
    } else {
        st.rid = 0;
    }
    SCRIPT_CMD_SUCCESS
}
