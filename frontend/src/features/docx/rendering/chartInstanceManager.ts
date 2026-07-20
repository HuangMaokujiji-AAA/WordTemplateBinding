import type * as echarts from "echarts";

/**
 * Central manager for ECharts instances created during document rendering.
 *
 * Responsibilities:
 *  - Track all active ECharts instances.
 *  - Track all active ResizeObservers.
 *  - Dispose all instances and observers on cleanup.
 *  - Prevent memory leaks when switching documents.
 */
class ChartInstanceManager {
  private instances: Map<string, echarts.ECharts> = new Map();
  private observers: ResizeObserver[] = [];

  /**
   * Register an ECharts instance with a unique slot ID.
   */
  register(slotId: string, instance: echarts.ECharts): void {
    // Dispose existing instance for this slot (if any)
    this.unregister(slotId);
    this.instances.set(slotId, instance);
  }

  /**
   * Unregister and dispose a single instance.
   */
  unregister(slotId: string): void {
    const instance = this.instances.get(slotId);
    if (instance && !instance.isDisposed()) {
      instance.dispose();
    }
    this.instances.delete(slotId);
  }

  /**
   * Register a ResizeObserver for cleanup.
   */
  addObserver(observer: ResizeObserver): void {
    this.observers.push(observer);
  }

  /**
   * Create a ResizeObserver for a chart instance and container.
   */
  observeResize(
    slotId: string,
    instance: echarts.ECharts,
    container: HTMLElement
  ): void {
    const observer = new ResizeObserver(() => {
      if (!instance.isDisposed()) {
        instance.resize();
      }
    });
    observer.observe(container);
    this.addObserver(observer);

    // Store association for cleanup
    this.observerMap.set(slotId, observer);
  }

  private observerMap: Map<string, ResizeObserver> = new Map();

  /**
   * Dispose all ECharts instances and disconnect all observers.
   */
  disposeAll(): void {
    // Dispose all chart instances
    for (const [_slotId, instance] of this.instances) {
      if (!instance.isDisposed()) {
        instance.dispose();
      }
    }
    this.instances.clear();

    // Disconnect all ResizeObservers
    for (const observer of this.observers) {
      observer.disconnect();
    }
    this.observers = [];
    this.observerMap.clear();
  }

  /**
   * Get the number of active instances.
   */
  get instanceCount(): number {
    return this.instances.size;
  }
}

/** Global singleton for the application. */
export const chartInstanceManager = new ChartInstanceManager();
