#!/bin/sh
set -eu
printf 'epoch_ms,rss_kib,hwm_kib,rss_anon_kib,rss_file_kib,rss_shmem_kib,cgroup_current_bytes,cgroup_peak_bytes,cgroup_anon_bytes,cgroup_file_bytes,cgroup_kernel_bytes,inactive_file_bytes,swap_bytes,cpu_usage_usec,nr_throttled,throttled_usec\n'
while :; do
    rss=0; hwm=0; rssanon=0; rssfile=0; rssshmem=0
    while read -r key value unit; do
        case "$key" in
            VmRSS:) rss=$value ;;
            VmHWM:) hwm=$value ;;
            RssAnon:) rssanon=$value ;;
            RssFile:) rssfile=$value ;;
            RssShmem:) rssshmem=$value ;;
        esac
    done < /proc/1/status
    read -r current < /sys/fs/cgroup/memory.current
    read -r peak < /sys/fs/cgroup/memory.peak
    read -r swap < /sys/fs/cgroup/memory.swap.current
    anon=0; file=0; kernel=0; inactive=0
    while read -r key value; do
        case "$key" in
            anon) anon=$value ;;
            file) file=$value ;;
            kernel) kernel=$value ;;
            inactive_file) inactive=$value ;;
        esac
    done < /sys/fs/cgroup/memory.stat
    usage=0; throttled=0; throttletime=0
    while read -r key value; do
        case "$key" in
            usage_usec) usage=$value ;;
            nr_throttled) throttled=$value ;;
            throttled_usec) throttletime=$value ;;
        esac
    done < /sys/fs/cgroup/cpu.stat
    printf '%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s\n' "$(date +%s%3N)" "$rss" "$hwm" "$rssanon" "$rssfile" "$rssshmem" "$current" "$peak" "$anon" "$file" "$kernel" "$inactive" "$swap" "$usage" "$throttled" "$throttletime"
    sleep 1
done
