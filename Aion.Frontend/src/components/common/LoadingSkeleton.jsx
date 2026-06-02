import './LoadingSkeleton.css';

export default function LoadingSkeleton({ rows = 5, height = 20 }) {
  return (
    <div className="skeleton-container">
      {Array.from({ length: rows }).map((_, i) => (
        <div 
          key={i} 
          className="skeleton-row" 
          style={{ 
            height, 
            width: `${60 + Math.random() * 30}%`,
            animationDelay: `${i * 0.1}s`
          }} 
        />
      ))}
    </div>
  );
}
