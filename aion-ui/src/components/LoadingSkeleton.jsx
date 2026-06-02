export default function LoadingSkeleton() {
  return (
    <div className="page">
      <div className="loading-skeleton">
        <div className="skeleton-header" />
        <div className="skeleton-grid">
          {[...Array(4)].map((_, i) => (
            <div key={i} className="skeleton-card">
              <div className="skeleton-row" />
              <div className="skeleton-row short" />
              <div className="skeleton-row" />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
