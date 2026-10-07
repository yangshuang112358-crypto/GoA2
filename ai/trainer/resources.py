"""Sample only this experiment's process tree; leave interactive capacity available."""
import os
import threading
import time
import psutil


class ResourceMonitor:
    def __init__(self):
        self.started = time.perf_counter()
        self.peak_mib = 0.
        self.min_available_mib = float("inf")
        self.cpu = {}
        self.stop_event = threading.Event()
        self.thread = threading.Thread(target=self._run, daemon=True)
        self.thread.start()

    def _run(self):
        while not self.stop_event.is_set():
            total = 0
            root = psutil.Process(os.getpid())
            for p in [root] + root.children(recursive=True):
                try:
                    total += p.memory_info().rss
                    c = p.cpu_times()
                    self.cpu[p.pid] = c.user + c.system
                except psutil.Error:
                    pass
            self.peak_mib = max(self.peak_mib, total / 2**20)
            self.min_available_mib = min(self.min_available_mib, psutil.virtual_memory().available / 2**20)
            self.stop_event.wait(1)

    def report(self):
        self.stop_event.set(); self.thread.join()
        seconds = time.perf_counter() - self.started
        return dict(process_tree_peak_rss_mib=self.peak_mib, system_min_available_mib=self.min_available_mib,
                    process_tree_cpu_seconds=sum(self.cpu.values()),
                    cpu_percent_of_machine=100*sum(self.cpu.values())/seconds/psutil.cpu_count(), sampled_seconds=seconds)


def require_interactive_memory():
    if psutil.virtual_memory().available < 4 * 2**30:
        raise MemoryError("Less than 4 GiB available; stopped at episode boundary to preserve interaction")
