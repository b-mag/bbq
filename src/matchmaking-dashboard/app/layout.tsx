import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'CARCOSA - Matchmaking Dashboard',
  description: 'Admin panel for monitoring game sessions, players, and analytics',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
