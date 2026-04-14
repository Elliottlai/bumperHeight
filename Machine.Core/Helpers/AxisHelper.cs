using Machine.Core.Enums;
using Machine.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Machine.Core
{
   public static class AxisHelper
   {
      public static double GetGap(this IAxis axis, double Position)
      {

         return Math.Abs(axis.GetRealPosition() - Position);
      }
      public static bool InErrorRange(this IAxis axis, double Position)
      {
         bool bInrange = false;
         if (Math.Abs(axis.GetRealPosition() - Position) < axis.Tolerance)
            bInrange = true;
         return bInrange;
      }

      public static bool VT_Move(this IAxis axis, double Position, bool IsAbsolute = true)
      {

         if (IsAbsolute)
            axis.MotMoveAbs(Position);
         else
            axis.MotMoveRel(Position);

         double Gap;
         do
         {
            Thread.Sleep(100);
            Gap = axis.GetRealPosition() - Position;
         } while (Math.Abs(Gap) >= axis.Tolerance);

         return !(axis.GetPLimit() || axis.GetNLimit());
      }

      public static void Home_Motion(this IAxis This)
      {
         if (This.HomeMode == 0)
         {

            This.MotStop();
            This.SetCurve(CurveType.T_Curve);
            This.SetMaxVel(This.OperationSpeed );
            This.SetStrVel(This.OperationStartSpeed);
            This.SetAccTime(This.OperationAcc );
            This.SetDecTime(This.OperationDec );
            Thread.Sleep(100);

            if (This.GetRealPosition()  > This.HomeBuffer *3  )
               This.MotMoveAbs(This.HomeBuffer * 3);

            while (true)
            {
               Thread.Sleep(100);
               if (This.Wait())
                  break;

            }

            This.SetMaxVel(This.OperationSpeed/3);
            This.SetStrVel(This.HomeStartSpeed);
            This.SetAccTime(This.HomeAcc);
            This.SetDecTime(This.HomeDec);

            if (!This.GetOrg())
            {
               Thread.Sleep(50);
               This.Home();

            }

            while (true)
            {
               Thread.Sleep(100);
               if (This.Wait())
                  break;

            }

            double Pos = 0;
            if (This.GetNLimit())
               Pos = This.HomeBuffer * 1.5;
            else if (This.GetOrg())
               Pos = This.HomeBuffer;

            This.MotMoveRel(Pos);

            while (true)
            {
               Thread.Sleep(100);
               if (This.Wait())
                  break;
            }

            This.SetMaxVel(This.HomeSpeed);

            This.Home();

            while (true)
            {
               Thread.Sleep(200);
               if (This.Wait())
                  break;
            }

            This.SetPosition(0);
            This.SetCurve(This.Curve);
            This.SetMaxVel(This.OperationSpeed);
            This.SetStrVel(This.OperationStartSpeed);
            This.SetAccTime(This.OperationAcc);
            This.SetDecTime(This.OperationDec);
         }
         /* else if (This.HomeMode == 4)
          {
              This.SetCurve(CurveType.T_Curve);
              This.SetMaxVel(This.HomeSpeed);
              This.SetStrVel(This.HomeStartSpeed);
              This.SetAccTime(This.HomeAcc);
              This.SetDecTime(This.HomeDec);
              double Pos = 0;
              if (This.GetOrg())
              {
                  Pos = This.GetLogicPosition() + This.HomeBuffer * 1000;
                  This.MotMoveAbs(Pos);
                  Thread.Sleep(50);
                  while (true)
                  {
                      if (This.Wait())
                          break;
                      Thread.Sleep(100);
                  }
              }

              This.Home();
              Thread.Sleep(50);

              while (true)
              {
                  if (This.Wait())
                      break;
                  Thread.Sleep(50);
              }

              if (This.GetNLimit())
              {
                  Pos = This.GetLogicPosition() + This.HomeBuffer * 1000 * 1.5;
                  This.MotMoveAbs(Pos);
                  Thread.Sleep(50);
                  while (true)
                  {
                      if (This.Wait())
                          break;
                      Thread.Sleep(100);

                  }
                  This.Home();
                  Thread.Sleep(50);
                  while (true)
                  {
                      if (This.Wait())
                          break;
                      Thread.Sleep(50);
                  }
              }
              This.SetPosition(0);
              This.SetCurve(This.Curve);
              This.SetMaxVel(This.OperationSpeed);
              This.SetStrVel(This.OperationStartSpeed);
              This.SetAccTime(This.OperationAcc);
              This.SetDecTime(This.OperationDec);


          }*/
         else
         {

            throw new Exception("Home Mode Error " + This.HomeMode);
         }
      }


   }
}
