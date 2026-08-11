import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'CARCOSA',
  description: 'A cooperative top-down RPG',
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
